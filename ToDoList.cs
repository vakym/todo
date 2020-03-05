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
            entry.AddState(timestamp, 0, usersWarehouse.GetUserById(userId), (currentEntry) =>
            {
                Entry newEntry = null;
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
            ProcessEntry(entryId, timestamp, userId, 1, (currentEntry) =>
            {
                return currentEntry != null ? Removable.CreateRemoved(currentEntry.Value) :
                                              Removable.CreateRemoved(Entry.Undone(entryId, "")) ;
            });
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId, timestamp, userId, 2, (currentEntry) =>
            {
                Entry newEntry = null;
                if (currentEntry != null)
                {
                    newEntry = currentEntry.Value.MarkDone();
                    return Removable.CreateNotRemoved(newEntry);
                }
                else
                {
                    newEntry = Entry.Done(entryId, "");
                    return Removable.CreateRemoved(newEntry);
                }
            });
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            ProcessEntry(entryId,timestamp,userId, 3, (currentEntry) =>
            {
                Entry newEntry = null;
                if (currentEntry != null)
                {
                    newEntry = currentEntry.Value.MarkUndone();
                    return Removable.CreateNotRemoved(newEntry);
                }
                else
                {
                    newEntry = Entry.Undone(entryId, "");
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
            foreach (var cEntry in entryWarehouse)
            {
                if (!cEntry.Value.Entry.Removed)
                    yield return cEntry.Value.Entry.Value;
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
                                 int typestateId,
                                 Func<Removable<Entry>, Removable<Entry>> action)
        {
            var entry = GetEntry(entryId);
            entry.AddState(timestamp, typestateId, usersWarehouse.GetUserById(userId), action);
        }

        private CalculatedEntry<long,Entry> GetEntry(int key)
        {
            if (entryWarehouse.TryGetValue(key, out var entry))
                return entry as CalculatedEntry<long, Entry>;
            entry = CalculatedEntry.Create<long, Entry>();
            SetConflicSolveRules(entry as CalculatedEntry<long, Entry>);
            entryWarehouse.Add(key, entry);
            return entry as CalculatedEntry<long, Entry>;
        }

        private void SetConflicSolveRules(CalculatedEntry<long, Entry> entry)
        {
            entry.AddConflicSolveRule((states) =>
            {
                var removeNodes = states.Nodes()
                                        .Where(node => node.Value.StateTypeId == 1)
                                        .ToList();
                foreach (var node in removeNodes)
                {
                    states.Remove(node);
                    states.AddLast(node);
                }
            }).AddConflicSolveRule((states) =>
            {
                var addNodes = states.Nodes()
                                     .Where(node => node.Value.StateTypeId == 0)
                                     .OrderByDescending(node => node.Value.User.Id);
                var first = addNodes.FirstOrDefault();
                var last = addNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    states.Remove(last);
                    states.AddAfter(first, last);
                }
            }).AddConflicSolveRule((states) => 
            {
                var statesNodes = states.Nodes()
                                        .Where(node => node.Value.StateTypeId == 2 
                                                       && node.Value.StateTypeId == 3)
                                        .OrderBy(node => node.Value.StateTypeId);
                var first = statesNodes.FirstOrDefault();
                var last = statesNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    states.Remove(first);
                    states.AddAfter(last, first);
                }
            });
        }
    }

    #region users
    public interface IUser
    {
        int Id { get; }

        bool IsDismiss { get; }

        void DismissUser();

        void AllowUser();
    }

    public class User : IUser
    {
        public int Id { get; }

        public bool IsDismiss { get; private set; }

        public User(int id)
        {
            Id = id;
        }

        public void DismissUser()
        {
            IsDismiss = true;
        }

        public void AllowUser()
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
            user.DismissUser();
        }

        public void AllowUser(int id)
        {
            var user = GetUserById(id);
            user.AllowUser();

        }
    }
    #endregion

    #region entry logic
    public class CalculatedEntry<TTimestamp,TEntry> where TTimestamp : struct
    {
        private readonly SortedDictionary<TTimestamp, LinkedList<ChangeState<Removable<TEntry>>>> states;
                      
        private readonly List<Action<LinkedList<ChangeState<Removable<TEntry>>>>> solveConflicRules 
                                      = new List<Action<LinkedList<ChangeState<Removable<TEntry>>>>>();
        private bool rebuildNeeded = true;

        private Removable<TEntry> entry;

        private void Build()
        {
            var firstAddState = states.FirstOrDefault(pair => pair.Value.First.Value.StateTypeId == 0);
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

        public CalculatedEntry()
        {
            states = new SortedDictionary<TTimestamp, LinkedList<ChangeState<Removable<TEntry>>>>();
        }

        public CalculatedEntry(IComparer<TTimestamp> comparer)
        {
            states = new SortedDictionary<TTimestamp, LinkedList<ChangeState<Removable<TEntry>>>>(comparer);
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
                             int typeId,
                             IUser user,
                             Func<Removable<TEntry>, Removable<TEntry>> action)
        {
            rebuildNeeded = true;
            var state = new ChangeState<Removable<TEntry>>(user, action, typeId);
            if(states.TryGetValue(timestamp, out var currentStates))
            {
                currentStates.AddLast(state);
                foreach (var rule in solveConflicRules)
                {
                    rule(currentStates);
                }
                return;
            }
            var linkedList = new LinkedList<ChangeState<Removable<TEntry>>>();
            linkedList.AddFirst(state);
            states.Add(timestamp, linkedList);
        }

        public CalculatedEntry<TTimestamp, TEntry> AddConflicSolveRule(
                                             Action<LinkedList<ChangeState<Removable<TEntry>>>> rule)
        {
            solveConflicRules.Add(rule);
            return this;
        }

        
    }

    public static class CalculatedEntry
    {
        public static CalculatedEntry<TTimeStamp,TEntry> Create<TTimeStamp, TEntry>() 
            where TTimeStamp : struct
        {
            return new CalculatedEntry<TTimeStamp, TEntry>();
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

        public int StateTypeId { get; }

        public Func<TEntry, TEntry> Change { get; }

        public ChangeState(IUser user,
                           Func<TEntry, TEntry> action,
                           int stateTypeId)
        {
            User = user;
            Change = action;
            StateTypeId = stateTypeId;
        }
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