using System;
using System.Threading;

namespace Unity.Services.Vivox
{
    internal class AsyncNoResult : IAsyncResult, IDisposable
    {
        EventWaitHandle _waitHandle;
        private System.Exception _exception;

        public AsyncNoResult(AsyncCallback callback)
        {
            Callback = callback;
            CompletedSynchronously = false;
        }

        public AsyncCallback Callback { get; set; }

        public object AsyncState { get; set; }
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_waitHandle == null)
                    _waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
                return _waitHandle;
            }
        }

        public void CheckForError()
        {
            if (_exception != null)
                throw _exception;
        }

        public bool CompletedSynchronously { get; set; }

        public bool IsCompleted { get; private set; }

        public void SetComplete()
        {
            if (IsCompleted)
                throw new InvalidOperationException();
            IsCompleted = true;
            Callback?.Invoke(this);
            var eventWaitHandle = AsyncWaitHandle as EventWaitHandle;
            eventWaitHandle?.Set();
        }

        public void SetCompletedSynchronously()
        {
            if (IsCompleted)
                throw new InvalidOperationException();
            CompletedSynchronously = true;
            IsCompleted = true;
            Callback?.Invoke(this);
            var eventWaitHandle = AsyncWaitHandle as EventWaitHandle;
            eventWaitHandle?.Set();
        }

        public void SetComplete(Exception exception)
        {
            if (IsCompleted)
                throw new InvalidOperationException();
            _exception = exception;
            SetComplete();
        }

        public void Dispose()
        {
            _waitHandle?.Close();
        }
    }

    internal class AsyncResult<T> : IAsyncResult, IDisposable
    {
        EventWaitHandle _waitHandle;
        private System.Exception _exception;
        private T _result;

        public AsyncResult(AsyncCallback callback)
        {
            IsCompleted = false;
            Callback = callback;
        }

        public T Result
        {
            get
            {
                if (_exception != null)
                    throw _exception;
                return _result;
            }
            private set
            {
                _result = value;
            }
        }

        public AsyncCallback Callback { get; set;  }

        public object AsyncState { get; set; }
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (_waitHandle == null)
                    _waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
                return _waitHandle;
            }
        }


        public bool CompletedSynchronously { get; set; }
        public bool IsCompleted { get; private set; }

        public void SetComplete(T result)
        {
            if (IsCompleted)
                throw new InvalidOperationException($"{GetType().Name}: Result already completed");
            Result = result;
            IsCompleted = true;
            Callback?.Invoke(this);
            (AsyncWaitHandle as EventWaitHandle)?.Set();
        }

        public void SetComplete(Exception exception)
        {
            if (IsCompleted)
                throw new InvalidOperationException($"{GetType().Name}: Result already completed");
            _exception = exception;
            IsCompleted = true;
            Callback?.Invoke(this);
            (AsyncWaitHandle as EventWaitHandle)?.Set();
        }

        public void SetCompletedSynchronously(T result)
        {
            if (IsCompleted)
                throw new InvalidOperationException();
            Result = result;
            CompletedSynchronously = true;
            IsCompleted = true;
            Callback?.Invoke(this);
            var eventWaitHandle = AsyncWaitHandle as EventWaitHandle;
            eventWaitHandle?.Set();
        }

        public void Dispose()
        {
            _waitHandle?.Close();
        }
    }
}
