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
        private readonly Dictionary<int, EntryHistory> entries = new Dictionary<int, EntryHistory>();

        private readonly UsersWarehouse usersWarehouse = new UsersWarehouse();

        public void AddEntry(int entryId, int userId, string name, long timestamp)
        {
            var user = usersWarehouse.GetUserById(userId);
            if(!entries.ContainsKey(entryId))
               entries.Add(entryId, EntryHistory.Create(entryId, user, name, timestamp));
            var a = new AddAction(timestamp, user);
            a.AddArgument("Name", name);
            entries[entryId].AddState(a);
        }

        public void RemoveEntry(int entryId, int userId, long timestamp)
        {
            var user = usersWarehouse.GetUserById(userId);
            if (entries.ContainsKey(entryId))
            {
                entries[entryId].AddState(new RemoveAction(timestamp, user));
            }
        }

        public void MarkDone(int entryId, int userId, long timestamp)
        {
            var user = usersWarehouse.GetUserById(userId);
            if (!entries.ContainsKey(entryId))
                entries.Add(entryId, EntryHistory.Create(entryId, user, "", timestamp));
            entries[entryId].AddState(new MarkDone(timestamp, user));
           
        }

        public void MarkUndone(int entryId, int userId, long timestamp)
        {
            var user = usersWarehouse.GetUserById(userId);
            if (!entries.ContainsKey(entryId))
                entries.Add(entryId, EntryHistory.Create(entryId, user, "", timestamp));
            entries[entryId].AddState(new MarkUnDone(timestamp, user));
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
            foreach (var item in entries)
            {
                if(item.Value.CurrentEntry != null)
                    yield return item.Value.CurrentEntry;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get => entries.Where(e => e.Value.CurrentEntry != null).Count(); }
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

    public class EntryHistory
    {
       
        public Entry CurrentEntry { get => Build(); }

        public bool IsRemoved { get; private set; }

        public int Id { get => baseEntry.Id; }

        private readonly Entry baseEntry;

        private List<IStateAction> states = new List<IStateAction>();

        public EntryHistory(int entryId, IUser user, string name, long timestamp)
        {
            baseEntry = Entry.Undone(entryId, name);
           // states.Add(new AddAction(timestamp, user));
        }

        public void AddState(IStateAction stateChanger)
        {
            states.Add(stateChanger);
            states = states.GroupBy(g => g.Timestamp)
                           .Select(x => Tuple.Create(x.Key,x.OrderBy(u=>u.User.Id)))
                           .OrderBy(key => key)
                           .SelectMany(t => SolveConflict(t.Item2))
                           .ToList();
        }

        public static EntryHistory Create(int entryId, IUser user, string name, long timestamp)
        {
            return new EntryHistory(entryId, user, name, timestamp);
        }


        private static IEnumerable<IStateAction> SolveConflict(IEnumerable<IStateAction> stateActions)
        {
            if(stateActions.Any(i=>i is RemoveAction))
            {
                yield return stateActions.First(i => i is RemoveAction);
                yield break;
            }
            if(stateActions.Any(i=> i is MarkUnDone))
            {
                stateActions = stateActions.Where(i => !(i is MarkDone));
            }
            var add = stateActions.Where(i => i is AddAction).OrderBy(i => i.User.Id).FirstOrDefault();
            if(add!=null)
                stateActions = stateActions.Where(i => !(i is AddAction)).Append(add);
            foreach (var item in stateActions)
            {
                yield return item;
            }
        }
        private Entry Build()
        {
            Entry currentEntry = baseEntry;
            var firstAdd = states.FirstOrDefault(i => i is AddAction);
            if (firstAdd!= null && firstAdd.User.IsDismiss)
            {
                return null;
            }
            foreach (var state in states)
            {
                if(!state.User.IsDismiss)
                    currentEntry = state.Invoke(currentEntry != null? currentEntry : baseEntry);
            }
            return currentEntry;
        }
    }

    public interface IStateAction
    {
        long Timestamp { get; }

        IUser User { get; }

        Entry Invoke(Entry entry);

        void AddArgument(string name, object value);
    }

    public class AddAction : IStateAction
    {
        private readonly Dictionary<string, object> arguments = new Dictionary<string, object>();
        public AddAction(long timestamp, IUser user)
        {
            Timestamp = timestamp;
            User = user;
        }

        public long Timestamp { get; }

        public IUser User { get; }

        public void AddArgument(string name, object value)
        {
            arguments.Add(name, value);
        }

        public Entry Invoke(Entry entry)
        { 
            if (arguments.ContainsKey("Name"))
                return new Entry(entry.Id, arguments["Name"].ToString(), entry.State);
            return entry;
        }
    }

    public class RemoveAction : IStateAction
    {
        public RemoveAction(long timestamp, IUser user)
        {
            Timestamp = timestamp;
            User = user;
        }

        public long Timestamp { get; }

        public IUser User { get; }

        public void AddArgument(string name, object value)
        {
            throw new NotImplementedException();
        }

        public Entry Invoke(Entry entry)
        {
            return null;
        }
    }

    public class MarkDone : IStateAction
    {
        public MarkDone(long timestamp, IUser user)
        {
            Timestamp = timestamp;
            User = user;
        }

        public long Timestamp { get; }

        public IUser User { get; }

        public void AddArgument(string name, object value)
        {
            throw new NotImplementedException();
        }

        public Entry Invoke(Entry entry)
        {
            return entry.MarkDone();
        }
    }

    public class MarkUnDone : IStateAction
    {
        public MarkUnDone(long timestamp, IUser user)
        {
            Timestamp = timestamp;
            User = user;
        }
        public long Timestamp { get; }

        public IUser User { get; }

        public void AddArgument(string name, object value)
        {
            throw new NotImplementedException();
        }

        public Entry Invoke(Entry entry)
        {
            return entry.MarkUndone();
        }
    }
}