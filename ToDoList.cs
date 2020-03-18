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
                    newEntry = currentEntry.State == EntryState.Done ? Entry.Done(entryId, name) :
                                                                       Entry.Undone(entryId, name);
                }
                else
                {
                    newEntry = Entry.Undone(entryId, name);
                }
                return newEntry;
            });
        }

        public void RemoveEntry(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, ActionType.Remove, (currentEntry) =>
            {
                return null;
            });
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, ActionType.Done, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    var newEntry = currentEntry.MarkDone();
                    return newEntry;
                }
                else
                {
                    return null;
                }
            });
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId,timestamp,userId, ActionType.Undone, (currentEntry) =>
            {
                if (currentEntry != null)
                {
                    var newEntry = currentEntry.MarkUndone();
                    return newEntry;
                }
                else
                {
                    return null;
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
                if (entry.Value.Entry != null)
                    yield return entry.Value.Entry;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entryWarehouse.Where(pair => pair.Value.Entry != null)
                                                .Count(); }

        private void ProcessEntry(int entryId,
                                 long timestamp,
                                 int userId,
                                 ActionType type,
                                 Func<Entry, Entry> action)
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

        private TEntry entry;

        private void Build()
        {
            var firstAdd = true;
            foreach (var state in states.Values)
            {
                foreach (var change in state.GetChanges())
                {
                    if (change.Type == ActionType.Add &&
                        firstAdd)
                    {
                        firstAdd = false;
                        if (change.User.IsDismiss)
                        {
                            entry = default;
                            return;
                        }
                    }

                    if (!change.User.IsDismiss)
                    {
                        entry = change.Apply(entry);
                    }
                }
            }
            rebuildNeeded = false;
        }

        public CalculatedEntry()
        {
            states = new SortedDictionary<TTimestamp, State<TEntry>>();
        }

        public TEntry Entry
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
                             Func<TEntry, TEntry> action)
        {
            rebuildNeeded = true;
            var state = new ChangeState<TEntry>(user, action, type);
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

    public class State<TEntry>
    {
        private Dictionary<ActionType, ChangeState<TEntry>> statesWihtSameTime = new Dictionary<ActionType, ChangeState<TEntry>>();

        private Dictionary<ActionType, Action<ChangeState<TEntry>>> conflictSolveRules 
                                                                        = new Dictionary<ActionType, Action<ChangeState<TEntry>>>();
       
        public State()
        {
            SetConflictSolveRules();
        }

        public void AddState(ChangeState<TEntry> state)
        {
            if (conflictSolveRules.TryGetValue(state.Type, out var rule))
                rule(state);
            else
                statesWihtSameTime.Add(state.Type, state);
        }

        public IEnumerable<ChangeState<TEntry>> GetChanges()
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
                if (statesWihtSameTime.ContainsKey(ActionType.Undone))
                    return;
                statesWihtSameTime.Add(newState.Type, newState);
            });

            conflictSolveRules.Add(ActionType.Undone, (newState) => {
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