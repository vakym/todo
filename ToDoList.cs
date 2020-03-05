using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
/// <summary>
/// Решение нуждаеться в глубоком рефакторинге. 
/// Это первая версия.
/// </summary>
namespace ToDoList
{
    public class ToDoList : IToDoList
    {
        

        private readonly UsersWarehouse usersWarehouse = new UsersWarehouse();
        private readonly Dictionary<int, ICalculatedEntity<Entry>> entities = new Dictionary<int, ICalculatedEntity<Entry>>();


        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            var entity = GetEntity(entryId);
           
        }

        public void RemoveEntry(int entryId, int userId, long timestamp)
        {
           
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            
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
                if (!cEntry.Value.IsRemoved)
                    yield return cEntry.Value.Entry;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entities.Where(entry => !entry.Value.IsRemoved)
                                          .Count(); }

        private CalculatedEntity<Entry> GetEntity(int key)
        {
            if (entities.TryGetValue(key, out var entity))
                return entity as CalculatedEntity<Entry>;
            entity = CalculatedEntity.Create<Entry>();
            SetConflicSolveRules(entity as CalculatedEntity<Entry>);
            entities.Add(key, entity);
            return entity as CalculatedEntity<Entry>;
        }

        private void SetConflicSolveRules(CalculatedEntity<Entry> entity)
        {

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
    //public class EntryWarehouse<TKey, TEntry> : IReadOnlyCollection<TEntry> 
    //{
    //    private readonly Dictionary<TKey, TEntry> entries = new Dictionary<TKey,TEntry>();

    //    public int Count => entries.Count;

    //    public void Add(TKey id, TEntry entity)
    //    {
    //        if (entries.ContainsKey(id)) throw new InvalidOperationException();
    //        entries.Add(id, entity);
    //    }

    //    public void RemoveById(TKey id)
    //    {
    //        if (!entries.ContainsKey(id)) throw new InvalidOperationException();
    //        entries.Remove(id);
    //    }

    //    public TEntry GetById(TKey id)
    //    {
    //        if (entries.TryGetValue(id, out var value))
    //            return value;
    //        return default;
    //    }

    //    public bool Contains(TKey key)
    //    {
    //        return entries.ContainsKey(key);
    //    }

    //    public IEnumerator<TEntry> GetEnumerator()
    //    {
    //        foreach (var item in entries.Values)
    //        {
    //                yield return item;
    //        }
    //    }

    //    IEnumerator IEnumerable.GetEnumerator()
    //    {
    //        return GetEnumerator();
    //    }
    //}
    public interface ICalculatedEntity<out TEntry>
    {
        bool IsRemoved { get; }
        TEntry Entry { get; }
    }

    public class CalculatedEntity<TEntry> : ICalculatedEntity<TEntry>
    {
        

        private LinkedList<Tuple<long, IStateChanger<IUser>>> states = new LinkedList<Tuple<long, IStateChanger<IUser>>>();

        private readonly List<Func<IStateChanger<IUser>>> solveConflicRules = new List<Func<IStateChanger<IUser>>>();

        private bool rebuildNeeded = true;

        private Entry entry;

        public CalculatedEntity()
        {
        }

        public bool IsRemoved { get; private set; }

        public TEntry Entry
        {
            get
            {
                if (rebuildNeeded)
                    Build();
                return entry;
            }
        }

        public CalculatedEntity<TEntry> AddConflicSolveRule(Func<IStateChanger<IUser>> rule)
        {
            solveConflicRules.Add(rule);
            return this;
        }

        private void Build()
        {

            foreach (var action in states)
            {
                if(!action.Item2.User.IsDismiss)
                {
                    entry = 
                }
            }
        }
    }

    public static class CalculatedEntity
    {
        public static CalculatedEntity<T> Create<T>()
        {
            return new CalculatedEntity<T>();
        }
    }

    public interface IStateChanger<TUser> where TUser : IUser
    {
        long TimeStamp { get; }
        TUser User { get; }
    }
 
}