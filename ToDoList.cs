using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

//*Исправлены замечания
//*Оптимизированы правила разрешения конфликтов
//*оптимизирован метод Build() теперь он проходит по коллекциям только один раз

namespace ToDoList
{
    public class ToDoList : IToDoList
    {
        private readonly UsersStorage users = new UsersStorage();
        private readonly Dictionary<int, CalculatedEntry<long, Entry>> entryWarehouse
                                          = new Dictionary<int, CalculatedEntry<long, Entry>>();

        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            var entry = GetEntry(entryId);
            entry.AddState(timestamp,
                           ActionType.Add,
                           users.GetUserById(userId),
                           (currentEntry) =>
            {
                Entry newEntry;
                if (currentEntry != null)
                {
                    newEntry = currentEntry.Value.State == EntryState.Done ? Entry.Done(entryId, name) :
                                                                             Entry.Undone(entryId, name);
                }
                else
                {
                    newEntry = Entry.Undone(entryId, name);
                }
                return Removable.CreateNotRemoved(newEntry);
            });
        }

        public void RemoveEntry(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, ActionType.Remove, (currentEntry) =>
            {
                return currentEntry != null ? Removable.CreateRemoved(currentEntry.Value) :
                                              Removable.CreateRemoved(Entry.Undone(entryId, ""));
            });
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, ActionType.Done, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    return Removable.CreateNotRemoved(currentEntry.Value.MarkDone());
                }
                else
                {
                    return Removable.CreateNotRemoved(Entry.Done(entryId, ""));
                }
            });
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId,timestamp,userId, ActionType.Undone, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    return Removable.CreateNotRemoved(currentEntry.Value.MarkUndone());
                }
                else
                {
                    return Removable.CreateNotRemoved(Entry.Undone(entryId, ""));
                }
            });
        }

        public void DismissUser(int userId)
        {
            users.DismissUser(userId);
        }

        public void AllowUser(int userId)
        {
            users.AllowUser(userId);
        }

        public IEnumerator<Entry> GetEnumerator()
        {
            foreach (var entry in entryWarehouse)
            {
                if (!entry.Value.Entry.Removed)
                    yield return entry.Value.Entry.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entryWarehouse.Where(pair => !pair.Value.Entry.Removed)
                                                .Count(); }

        private void ProcessEntry(int entryId,
                                 long timestamp,
                                 int userId,
                                 ActionType type,
                                 Func<Removable<Entry>, Removable<Entry>> action)
        {
            var entry = GetEntry(entryId);
            entry.AddState(timestamp, type, users.GetUserById(userId), action);
        }

        private CalculatedEntry<long,Entry> GetEntry(int entryId)
        {
            if (entryWarehouse.TryGetValue(entryId, out var entry))
                return entry;
            entry = new CalculatedEntry<long, Entry>();
            entryWarehouse.Add(entryId, entry);
            return entry;
        }
    }

    #region users
    public interface IUser
    {
        int Id { get; }

        bool IsDismiss { get; }

        void Dismiss();

        void Allow();
    }

    public class User : IUser
    {
        public int Id { get; }

        public bool IsDismiss { get; private set; }

        public User(int id)
        {
            Id = id;
        }

        public void Dismiss()
        {
            IsDismiss = true;
        }

        public void Allow()
        {
            IsDismiss = false;
        }
    }

    public class UsersStorage
    {
        private readonly Dictionary<int, IUser> users = new Dictionary<int, IUser>();

        public IUser GetUserById(int id)
        {
            if(users.TryGetValue(id,out var user))
            {
                return user;
            }
            user = new User(id);
            users.Add(id,user);
            return user;
        }

        public void DismissUser(int id)
        {
            var user = GetUserById(id);
            user.Dismiss();
        }

        public void AllowUser(int id)
        {
            var user = GetUserById(id);
            user.Allow();

        }
    }
    #endregion

    #region entry logic
    public class CalculatedEntry<TTimestamp,TEntry>
    {
        private readonly SortedDictionary<TTimestamp, State<Removable<TEntry>>> states 
                                          = new SortedDictionary<TTimestamp, State<Removable<TEntry>>>();

        private bool rebuildNeeded = true;

        private Removable<TEntry> entry;

        private void Build()
        {
            var isFirstAddAction = true;
            foreach (var state in states.Values)
            {
                foreach (var change in state.GetChanges())
                {
                    if (change.Type == ActionType.Add &&
                        isFirstAddAction)
                    {
                        isFirstAddAction = false;
                        if (change.User.IsDismiss)
                        {
                            entry = Removable.CreateRemoved(default(TEntry));
                            return;
                        }
                    }

                    if (!change.User.IsDismiss)
                    {
                        entry = change.Apply(entry);
                    }
                }
            }

            if (isFirstAddAction)
                entry = Removable.CreateRemoved(default(TEntry));
            rebuildNeeded = false;
        }

        public Removable<TEntry> Entry
        {
            get
            {
                if (rebuildNeeded)
                    Build();
                return entry;
            }
        }

        public void AddState(TTimestamp timestamp,
                             ActionType type,
                             IUser user,
                             Func<Removable<TEntry>, Removable<TEntry>> action)
        {
            rebuildNeeded = true;
            var change = new ChangeState<Removable<TEntry>>(user, action, type);
            if(states.TryGetValue(timestamp, out var currentStates))
            {
                currentStates.AddChange(change);
                return;
            }
            var newState = new State<Removable<TEntry>>();
            newState.AddChange(change);
            states.Add(timestamp, newState);
        }
    }
    
    public class ChangeState<TEntry>
    { 
        public IUser User { get; }

        public ActionType Type { get; }

        public Func<TEntry, TEntry> Apply { get; }

        public ChangeState(IUser user,
                           Func<TEntry, TEntry> action,
                           ActionType type)
        {
            User = user;
            Apply = action;
            Type = type;
        }
    }

    public class Removable<TEntity>
    {
        public bool Removed { get; }

        public TEntity Value { get; }

        public Removable(bool removed, TEntity value)
        {
            Removed = removed;
            Value = value;
        }
    }

    public static class Removable
    {
        public static Removable<TEntity> CreateRemoved<TEntity>(TEntity value)
                                                => new Removable<TEntity>(true, value);
        public static Removable<TEntity> CreateNotRemoved<TEntity>(TEntity value)
                                                => new Removable<TEntity>(false, value);
    }


    public class State<TEntry>
    {
        private readonly SortedDictionary<ActionType, ChangeState<TEntry>> changesWihtSameTime 
                                             = new SortedDictionary<ActionType, ChangeState<TEntry>>();

        private readonly Dictionary<ActionType, Action<ChangeState<TEntry>>> conflictSolveRules 
                                             = new Dictionary<ActionType, Action<ChangeState<TEntry>>>();
       
        public State()
        {
            SetConflictSolveRules();
        }

        public void AddChange(ChangeState<TEntry> change)
        {
            if (conflictSolveRules.TryGetValue(change.Type, out var rule))
                rule(change);
            else
                changesWihtSameTime.Add(change.Type, change);
        }

        public IEnumerable<ChangeState<TEntry>> GetChanges()
        {
            foreach (var change in changesWihtSameTime)
            {
                yield return change.Value;
            }
        }

        private void SetConflictSolveRules()
        {
            conflictSolveRules.Add(ActionType.Remove, (newState) => {
                changesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Add, (newState) => {
                if (changesWihtSameTime.TryGetValue(newState.Type, out var change))
                {
                    if (change.User.Id > newState.User.Id)
                        changesWihtSameTime[newState.Type] = newState;
                }
                else
                    changesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Done, (newState) => {
                if (changesWihtSameTime.ContainsKey(ActionType.Undone))
                    return;
                changesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Undone, (newState) => {
                changesWihtSameTime.Remove(ActionType.Done);
                changesWihtSameTime.Add(newState.Type, newState);
            });
        }
    }
    #endregion
    public enum ActionType
    {
        Add,
        Remove,
        Done,
        Undone,
    }
}