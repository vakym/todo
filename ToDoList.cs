using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ToDoList
{
    public class ToDoList : IToDoList
    {
        private readonly UsersWarehouse usersWarehouse = new UsersWarehouse();
        private readonly Dictionary<int, CalculatedEntry<long, Entry>> entryWarehouse
                                          = new Dictionary<int, CalculatedEntry<long, Entry>>();

        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            var entry = GetEntry(entryId);
            entry.AddState(timestamp, ActionType.Add, usersWarehouse.GetUserById(userId), (currentEntry) =>
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
                                              Removable.CreateRemoved(Entry.Undone(entryId, "")) ;
            });
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, ActionType.Done, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    var newEntry = currentEntry.Value.MarkDone();
                    return Removable.CreateNotRemoved(newEntry);
                }
                else
                {
                    var newEntry = Entry.Done(entryId, "");
                    return Removable.CreateRemoved(newEntry);
                }
            });
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId,timestamp,userId, ActionType.Undone, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    var newEntry = currentEntry.Value.MarkUndone();
                    return Removable.CreateNotRemoved(newEntry);
                }
                else
                {
                    var newEntry = Entry.Undone(entryId, "");
                    return Removable.CreateRemoved(newEntry);
                }
            });
        }

        public void DismissUser(int userId)
        {
            usersWarehouse.DismissUser(userId);
        }

        public void AllowUser(int userId)
        {
            usersWarehouse.AllowUser(userId);
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
            entry.AddState(timestamp, type, usersWarehouse.GetUserById(userId), action);
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

    public class UsersWarehouse
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
        private readonly SortedDictionary<TTimestamp, State<TEntry>> states;
                      
        private bool rebuildNeeded = true;

        private Removable<TEntry> entry;

        private void Build()
        {
            //var firstAddState = states.FirstOrDefault(pair => pair.Value == ActionType.Add);
            //if (firstAddState.Value != null)
            //{
            //    var firstAddStateUser = firstAddState.Value.First.Value.User;
            //    if(firstAddStateUser.IsDismiss)
            //    {
            //        entry = Removable.CreateRemoved(default(TEntry));
            //        return;
            //    }
            //}
            foreach (var actions in states.Values)
            {
                foreach (var action in actions.GetChanges())
                {
                    if (!action.User.IsDismiss)
                    {
                        entry = action.Change(entry);
                    }
                }
            }
            rebuildNeeded = false;
        }

        public CalculatedEntry()
        {
            states = new SortedDictionary<TTimestamp, State<TEntry>>();
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
                             Func<Removable<TEntry>,
                             Removable<TEntry>> action)
        {
            rebuildNeeded = true;
            var state = new ChangeState<Removable<TEntry>>(user, action, type);
            if(states.TryGetValue(timestamp, out var currentStates))
            {
                currentStates.AddState(state);
                return;
            }
            var linkedList = new State<TEntry>();
            linkedList.AddState(state);
            states.Add(timestamp, linkedList);
        }
    }
    #endregion
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

    public class ChangeState<TEntry>
    { 
        public IUser User { get; }

        public ActionType Type { get; }

        public Func<TEntry, TEntry> Change { get; }

        public ChangeState(IUser user,
                           Func<TEntry, TEntry> action,
                           ActionType type)
        {
            User = user;
            Change = action;
            Type = type;
        }
    }

    public class State<TEntry>
    {
        private Dictionary<ActionType, ChangeState<Removable<TEntry>>> statesWihtSameTime = new Dictionary<ActionType, ChangeState<Removable<TEntry>>>();

        private Dictionary<ActionType, Action<ChangeState<Removable<TEntry>>>> conflictSolveRules = new Dictionary<ActionType, Action<ChangeState<Removable<TEntry>>>>();
       
        public State()
        {
            SetConflictSolveRules();
        }

        public void AddState(ChangeState<Removable<TEntry>> state)
        {
            if (conflictSolveRules.TryGetValue(state.Type, out var rule))
                rule(state);
            else
                statesWihtSameTime.Add(state.Type, state);
        }

        public IEnumerable<ChangeState<Removable<TEntry>>> GetChanges()
        {
            foreach (var state in statesWihtSameTime)
            {
                yield return state.Value;
            }
        }

        private void SetConflictSolveRules()
        {
            conflictSolveRules.Add(ActionType.Remove, (newState) => {
                statesWihtSameTime.Clear();
                statesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Add, (newState) => {
                if (statesWihtSameTime.ContainsKey(ActionType.Remove))
                    return;
                if (statesWihtSameTime.TryGetValue(newState.Type, out var change))
                {
                    if (change.User.Id > newState.User.Id)
                        statesWihtSameTime[newState.Type] = newState;
                }
                else
                    statesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Done, (newState) => {
                if (statesWihtSameTime.ContainsKey(ActionType.Remove) ||
                    statesWihtSameTime.ContainsKey(ActionType.Undone))
                    return;
                statesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Undone, (newState) => {
                if (statesWihtSameTime.ContainsKey(ActionType.Remove))                    
                    return;
                statesWihtSameTime.Remove(ActionType.Done);
                statesWihtSameTime.Add(newState.Type, newState);
            });
        }
    }

    public enum ActionType
    {
        Add,
        Remove,
        Done,
        Undone
    }
}