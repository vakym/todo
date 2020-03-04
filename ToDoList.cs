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
        private readonly EntryWarehouse<int, CalculatedEntity<long, Entry>> entities = new EntryWarehouse<int, CalculatedEntity<long, Entry>>();


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
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get; }
    }

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

    public interface ICalculatedEntity<out TEntry>
    {
        bool IsRemoved { get; }
        TEntry Entry { get; }
    }

    public class CalculatedEntity<TTimestamp,TEntry> : ICalculatedEntity<TEntry> where TTimestamp : struct
    {
        public bool IsRemoved { get; private set; }

        public TEntry Entry => throw new NotImplementedException();

        private readonly ConflictSolver<IStateChanger<TTimestamp, IUser>> conflictSolver 
                                                        = new ConflictSolver<IStateChanger<TTimestamp, IUser>>();

        public CalculatedEntity(ConflictSolver<IStateChanger<TTimestamp, IUser>> conflictSolver)
        {
            this.conflictSolver = conflictSolver;
        }
    }

    public interface IStateChanger<TTimeStamp,TUser> where TUser : IUser
    {
        TTimeStamp TimeStamp { get; }
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


    public class EntryWarehouse<TKey, TEntry> : IReadOnlyCollection<TEntry> where TEntry : ICalculatedEntity<TEntry>
    {
        private readonly Dictionary<TKey, TEntry> entries = new Dictionary<TKey, TEntry>();

        public int Count => throw new NotImplementedException();

        public void Add(TKey id, TEntry entry)
        {
            if (id == default) throw new ArgumentException();
            if (entries.ContainsKey(id)) throw new InvalidOperationException();
            entries.Add(id, entry);
        }

        public void RemoveById(TKey id)
        {
            if (id == default) throw new ArgumentException();
            if (!entries.ContainsKey(id)) throw new InvalidOperationException();
            entries.Remove(id);
        }

        public TEntry GetById(TKey id)
        {
            if (id == default) throw new ArgumentException();
            if (entries.TryGetValue(id, out var entry))
                return entry;
            return default;
        }

        public IEnumerator<TEntry> GetEnumerator()
        {
            foreach (var item in entries.Values)
            {
                if (!item.IsRemoved)
                    yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    
}