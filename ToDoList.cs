using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ToDoList
{
    public class ToDoList : IToDoList
    {
        private readonly UsersWarehouse usersWarehouse = new UsersWarehouse();
        private readonly Dictionary<int, CalculatedEntity<long, Entry>> entities
                                                        = new Dictionary<int, CalculatedEntity<long, Entry>>();

        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            var entity = GetEntity(entryId);
            entity.AddState(timestamp,0, usersWarehouse.GetUserById(userId), (currentState) =>
            {
                Entry newEntry = null;
                if (currentState != null)
                {
                    newEntry = currentState.Value.State == EntryState.Done ? Entry.Done(entryId, name) :
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
            var entity = GetEntity(entryId);
            entity.AddState(timestamp,1, usersWarehouse.GetUserById(userId), (currentState) =>
            {
                return currentState!= null ?  Removable.CreateRemoved(currentState.Value) :
                                              Removable.CreateRemoved(Entry.Undone(entryId, "")) ;
            });
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            var entity = GetEntity(entryId);
            entity.AddState(timestamp,2, usersWarehouse.GetUserById(userId), (currentState) =>
            {
                Entry newEntry = null;
                if (currentState != null)
                {
                    newEntry = currentState.Value.MarkDone();
                }
                else
                {
                    newEntry = Entry.Done(entryId, "");
                }
                return Removable.CreateRemoved(newEntry);
            });
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            var entity = GetEntity(entryId);
            entity.AddState(timestamp,3, usersWarehouse.GetUserById(userId), (currentState) =>
            {
                Entry newEntry = null;
                if (currentState != null)
                {
                    newEntry = currentState.Value.MarkUndone();
                }
                else
                {
                    newEntry = Entry.Undone(entryId, "");
                }
                return Removable.CreateNotRemoved(newEntry);
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
            foreach (var cEntry in entities)
            {
                if (!cEntry.Value.Entry.Removed)
                    yield return cEntry.Value.Entry.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entities.Where(entry => !entry.Value.Entry.Removed)
                                          .Count(); }

        private CalculatedEntity<long,Entry> GetEntity(int key)
        {
            if (entities.TryGetValue(key, out var entity))
                return entity as CalculatedEntity<long, Entry>;
            entity = CalculatedEntity.Create<long, Entry>();
            SetConflicSolveRules(entity as CalculatedEntity<long, Entry>);
            entities.Add(key, entity);
            return entity as CalculatedEntity<long, Entry>;
        }

        private void SetConflicSolveRules(CalculatedEntity<long, Entry> entity)
        {
            entity.AddConflicSolveRule((actions) =>
            {
                var removeNodes = actions.Nodes()
                                         .Where(n => n.Value.StateTypeId == 1)
                                         .ToList();
                foreach (var node in removeNodes)
                {
                    actions.Remove(node);
                    actions.AddLast(node);
                }
            }).AddConflicSolveRule((actions) =>
            {
                var addNodes = actions.Nodes()
                                      .Where(n => n.Value.StateTypeId == 0)
                                      .OrderByDescending(n => n.Value.User.Id);
                var first = addNodes.FirstOrDefault();
                var last = addNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    actions.Remove(last);
                    actions.AddAfter(first, last);
                }
            }).AddConflicSolveRule((actions) => 
            {
                var statesNodes = actions.Nodes()
                                         .Where(n => n.Value.StateTypeId == 2 && n.Value.StateTypeId == 3)
                                         .OrderBy(n => n.Value.StateTypeId);
                var first = statesNodes.FirstOrDefault();
                var last = statesNodes.LastOrDefault();
                if (first != null && first != last)
                {
                    actions.Remove(first);
                    actions.AddAfter(last, first);
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

    public class CalculatedEntity<TTimestamp,TEntry>
    {
        private SortedDictionary<TTimestamp, LinkedList<StateChanger<Removable<TEntry>>>> states 
                                                                    = new SortedDictionary<TTimestamp, LinkedList<StateChanger<Removable<TEntry>>>>();

        private readonly List<Action<LinkedList<StateChanger<Removable<TEntry>>>>> solveConflicRules 
                                                                       = new List<Action<LinkedList<StateChanger<Removable<TEntry>>>>>();
        private bool rebuildNeeded = true;

        private Removable<TEntry> entry;

        public Removable<TEntry> Entry
        {
            get
            {
                if (rebuildNeeded)
                    Build();
                return entry;
            }
        }

        public void AddState(TTimestamp timestamp,int typeId, IUser user, Func<Removable<TEntry>, Removable<TEntry>> action)
        {
            rebuildNeeded = true;
            var state = new StateChanger<Removable<TEntry>>(user, action, typeId);
            if(states.TryGetValue(timestamp, out var actions))
            {
                actions.AddLast(state);
                foreach (var rule in solveConflicRules)
                {
                    rule(actions);
                }
                return;
            }
            var linkedList = new LinkedList<StateChanger<Removable<TEntry>>>();
            linkedList.AddFirst(state);
            states.Add(timestamp, linkedList);
        }

        public CalculatedEntity<TTimestamp, TEntry> AddConflicSolveRule(Action<LinkedList<StateChanger<Removable<TEntry>>>> rule)
        {
            solveConflicRules.Add(rule);
            return this;
        }

        private void Build()
        {
            var firstAddAction = states.FirstOrDefault(v => v.Value.First.Value.StateTypeId == 0);
            if( firstAddAction.Value != null)
            {
                var firstAddActionUser = firstAddAction.Value.First.Value.User;
                if(firstAddActionUser.IsDismiss)
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
                        entry = action.Action(entry);
                    }
                }
            }
            rebuildNeeded = false;
        }
    }

    public static class CalculatedEntity
    {
        public static CalculatedEntity<TTimeStamp,TEntry> Create<TTimeStamp, TEntry>() 
        {
            return new CalculatedEntity<TTimeStamp, TEntry>();
        }
    }

    public class Removable<TEntry>
    {
        public bool Removed { get; }

        public TEntry Value { get; }

        public Removable(bool removed, TEntry value)
        {
            Removed = removed;
            Value = value;
        } 
    }

    public static class Removable
    {
        public static Removable<T> CreateRemoved<T>(T value) 
                                                => new Removable<T>(true, value);
        public static Removable<T> CreateNotRemoved<T>(T value)
                                                => new Removable<T>(false, value);
    }

    public class StateChanger<TEntry>
    { 
        public IUser User { get; }

        public int StateTypeId { get; }

        public Func<TEntry, TEntry> Action { get; }

        public StateChanger(IUser user, Func<TEntry, TEntry> action, int stateTypeId)
        {
            User = user;
            Action = action;
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