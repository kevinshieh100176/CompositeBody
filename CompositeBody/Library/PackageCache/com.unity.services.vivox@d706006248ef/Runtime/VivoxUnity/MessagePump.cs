using System;
using System.Threading;


namespace Unity.Services.Vivox
{
    internal class MessagePump
    {
        internal delegate void RunLoop(ref bool didWork);

        static MessagePump _instance;

        MessagePump() {}

        public event RunLoop MainLoopRun;

        public static MessagePump Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MessagePump();
                }

                return _instance;
            }
        }

        public void RunUntil(Func<bool> done)
        {
            for (;;)
            {
                RunOnce();
                if (!done())
                {
                    Thread.Sleep(20);
                }
                else
                {
                    break;
                }
            }
        }

        public void RunOnce()
        {
            for (;;)
            {
                var didWork = false;
                MainLoopRun?.Invoke(ref didWork);
                if (didWork)
                {
                    continue;
                }

                break;
            }
        }

        public static bool IsDone(WaitHandle handle, DateTime until)
        {
            if (handle != null)
            {
                if (handle.WaitOne(0))
                {
                    return true;
                }
            }
            if (DateTime.Now >= until)
            {
                return true;
            }

            return false;
        }

        public static bool Run(WaitHandle handle, TimeSpan until)
        {
            var then = DateTime.Now + until;
            Instance.RunUntil(() => IsDone(handle, then));
            if (handle != null)
            {
                return handle.WaitOne(0);
            }

            return false;
        }

        public delegate bool DoneDelegate();
    }
}
