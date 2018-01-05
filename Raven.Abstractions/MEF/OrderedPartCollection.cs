#if !DNXCORE50
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;

namespace Raven.Abstractions.MEF
{
    public class OrderedPartCollection<T> : ICollection<Lazy<T, IPartMetadata>>, INotifyCollectionChanged
    {
        private readonly ObservableCollection<Lazy<T, IPartMetadata>> inner = new ObservableCollection<Lazy<T, IPartMetadata>>();
        private ThreadLocal<DisableTriggerState> disableApplication;

        public OrderedPartCollection<T> Init(ThreadLocal<DisableTriggerState> disableApplicationValue)
        {
            disableApplication = disableApplicationValue;
            return this;
        }
        
        public List<Lazy<T, IPartMetadata>> GetAllParts()
        {
            return inner.ToList();
        }
        
        public IEnumerator<Lazy<T, IPartMetadata>> GetEnumerator()
        {
            try
            {
                if (disableApplication != null && disableApplication.Value.Disabled)
                {
                    if (disableApplication.Value.Except != null)
                    {
                        var exceptList = new List<Lazy<T, IPartMetadata>>();
                        foreach (var part in inner)
                        {
                            if (disableApplication.Value.Except.Contains(part.Value?.GetType()))
                            {
                                exceptList.Add(part);
                            }
                        }
                        return exceptList.GetEnumerator();
                    }
                    return Enumerable.Empty<Lazy<T, IPartMetadata>>().GetEnumerator();
                }
            }
            catch (ObjectDisposedException) // The database was disposed while we got here
            {
                return Enumerable.Empty<Lazy<T, IPartMetadata>>().GetEnumerator();
            }

            return inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            Add(new Lazy<T, IPartMetadata>(() => item, new PartMetadata {Order = 0}));
        }

        public void Add(Lazy<T, IPartMetadata> item)
        {
            int insertAt = inner.TakeWhile(lazy => item.Metadata.Order >= lazy.Metadata.Order).Count();
            // force the lazy to create its value.
            // the reason we are doing that is that otherwise, we run into errors if we have multi thread creations of the item
            GC.KeepAlive(item.Value);
            inner.Insert(insertAt, item);
        }

        public void Clear()
        {
            inner.Clear();
        }

        public bool Contains(Lazy<T, IPartMetadata> item)
        {
            return inner.Contains(item);
        }

        public void CopyTo(Lazy<T, IPartMetadata>[] array, int arrayIndex)
        {
            inner.CopyTo(array, arrayIndex);
        }

        public bool Remove(Lazy<T, IPartMetadata> item)
        {
            return inner.Remove(item);
        }

        public int Count
        {
            get { return inner.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged
        {
            add { inner.CollectionChanged += value; }
            remove { inner.CollectionChanged -= value; }
        }

        public IEnumerable<TResult> OfType<TResult>()
        {
            return this.Select(x => x.Value).OfType<TResult>();			
        }

        public IEnumerable<TResult> Select<TResult>(Func<T, TResult> func)
        {
            return this.Select(x => func(x.Value));
        }

        public TAccumulate Aggregate<TAccumulate>(TAccumulate seed, Func<TAccumulate, T, TAccumulate> func)
        {
            return this.Aggregate(seed, (accumulate, lazy) => func(accumulate,lazy.Value));
        }

        public void Apply(Action<T> action)
        {
            foreach (var item in this)
            {
                action(item.Value);
            }
        }

    }
}
#endif