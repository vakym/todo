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
        private readonly SortedDictionary<TTimestamp, LinkedList<ChangeState<Removable<TEntry>>>> states;
                      
        private readonly List<Action<LinkedList<ChangeState<Removable<TEntry>>>>> solveConflictRules 
                                      = new List<Action<LinkedList<ChangeState<Removable<TEntry>>>>>();
        private bool rebuildNeeded = true;

        private Removable<TEntry> entry;

        private void Build()
        {
            var firstAddState = states.FirstOrDefault(pair => pair.Value.First.Value.Type == ActionType.Add);
            if (firstAddState.Value != null)
            {
                var firstAddStateUser = firstAddState.Value.First.Value.User;
                if(firstAddStateUser.IsDismiss)
                {
                    entry = Removable.CreateRemoved(default(TEntry));
                    return;
                }
            }
            foreach (var actions in states.Values)
            {
                foreach (var action in actions)
                {
                    if (!action.User.IsDismiss)
                    {
                        entry = action.Change(entry);
                    }
                }
            }
            rebuildNeeded = false;
        }

        private void SetConflicSolveRules()
        {
            solveConflictRules.Add((states) =>
            {
                var removeNodes = states.Nodes()
                                        .Where(node => node.Value.Type == ActionType.Remove)
                                        .ToList();
                foreach (var node in removeNodes)
                {
                    states.Remove(node);
                    states.AddLast(node);
                }
            });
            solveConflictRules.Add((states) =>
            {
                var addNodes = states.Nodes()
                                     .Where(node => node.Value.Type == 0)
                                     .OrderByDescending(node => node.Value.User.Id);
                var first = addNodes.FirstOrDefault();
                var last = addNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    states.Remove(last);
                    states.AddAfter(first, last);
                }
            });
            solveConflictRules.Add((states) =>
            {
                var statesNodes = states.Nodes()
                                        .Where(node => node.Value.Type == ActionType.Done
                                                       && node.Value.Type == ActionType.Undone)
                                        .OrderBy(node => node.Value.Type);
                var first = statesNodes.FirstOrDefault();
                var last = statesNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    states.Remove(first);
                    states.AddAfter(last, first);
                }
            });
        }

        public CalculatedEntry()
        {
            states = new SortedDictionary<TTimestamp, LinkedList<ChangeState<Removable<TEntry>>>>();
            SetConflicSolveRules();
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
                currentStates.AddLast(state);
                foreach (var rule in solveConflictRules)
                {
                    rule(currentStates);
                }
                return;
            }
            var linkedList = new LinkedList<ChangeState<Removable<TEntry>>>();
            linkedList.AddFirst(state);
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

    }

    public enum ActionType
    {
        Add,
        Remove,
        Done,
        Undone
    }

    public static class LinkedListExtensions
    {
        public static IEnumerable<LinkedListNode<T>> Nodes<T>(this LinkedList<T> list)
        {
            var node = list.First;
            while (node != null)
            {
                yield return node;
                node = node.Next;
            }
        }
    }
}