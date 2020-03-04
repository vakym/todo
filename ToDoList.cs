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
        private readonly EntryWarehouse<int, Entry> entities = new EntryWarehouse<int, Entry>();


        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            
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
                if (!cEntry.IsRemoved)
                    yield return cEntry.Entry;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entities.Where(entry => !entry.IsRemoved)
                                          .Count(); }
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

    public interface ICalculatedEntity<out TEntry>
    {
        bool IsRemoved { get; }
        TEntry Entry { get; }
    }

    public class CalculatedEntity<TEntry> : ICalculatedEntity<TEntry>
    {
        public bool IsRemoved { get; private set; }

        public TEntry Entry => throw new NotImplementedException();

        public SortedList<long,IStateChanger<IUser>> states = new SortedList<long,IStateChanger<IUser>>();

        private readonly IConflictSolver<IStateChanger<IUser>> conflictSolver;                                          

        public CalculatedEntity(IConflictSolver<IStateChanger<IUser>> conflictSolver)
        {
            this.conflictSolver = conflictSolver;
        }

       
    }

    public interface IStateChanger<TUser> where TUser : IUser
    {
        long TimeStamp { get; }
        TUser User { get; }
    }

    public interface IConflictSolver<TState>
    {
        IEnumerable<TState> Solve(IEnumerable<TState> states);
    }

    public class ConflictSolver<TState> : IConflictSolver<TState>
    {
        public IEnumerable<TState> Solve(IEnumerable<TState> states)
        {
            throw new NotImplementedException();
        }
    }


    public class EntryWarehouse<TKey, TEntry> : IReadOnlyCollection<ICalculatedEntity<TEntry>> 
    {
        private readonly Dictionary<TKey, ICalculatedEntity<TEntry>> entries = new Dictionary<TKey, ICalculatedEntity<TEntry>>();

        public int Count => entries.Count;

        public void Add(TKey id, ICalculatedEntity<TEntry> entity)
        {
            if (entries.ContainsKey(id)) throw new InvalidOperationException();
            entries.Add(id, entity);
        }

        public void RemoveById(TKey id)
        {
            if (!entries.ContainsKey(id)) throw new InvalidOperationException();
            entries.Remove(id);
        }

        public TEntry GetById(TKey id)
        {
            if (entries.TryGetValue(id, out var value))
                return value.Entry;
            return default;
        }

        public IEnumerator<ICalculatedEntity<TEntry>> GetEnumerator()
        {
            foreach (var item in entries.Values)
            {
                    yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    
}