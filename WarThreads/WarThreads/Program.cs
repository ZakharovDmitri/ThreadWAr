using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Activities;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using HANDLE = System.IntPtr;

namespace WarThreads
{
    public struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
    public static class ConsoleHelper
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll")]
        public static extern bool SetEvent(IntPtr hEvent);


        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WriteConsoleOutputCharacter(IntPtr hConsoleOutput, string lpCharacter, uint nLength, Point16 dwWriteCoord, out uint lpNumberOfCharsWritten);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_INPUT_HANDLE = -10;
        private const int STD_ERROR_HANDLE = -12;
        private static readonly IntPtr _stdOut = GetStdHandle(STD_OUTPUT_HANDLE);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point16
        {
            public short X;
            public short Y;

            public Point16(short x, short y)
                => (X, Y) = (x, y);
        };

        public static string ReadCharacter(int x, int y)
        {
            StringBuilder result = new StringBuilder(1);
            ReadConsoleOutputCharacter(_stdOut, result, 1, new Point16((short)x, (short)y), out uint _);
            return result.ToString();
        }

        public static void WriteToBufferAt(string text, int x, int y)
        {
            WriteConsoleOutputCharacter(_stdOut, text, (uint)text.Length, new Point16((short)x, (short)y), out uint _);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ReadConsoleOutputCharacter(IntPtr hConsoleOutput, [Out] StringBuilder lpCharacter, uint length, Point16 bufferCoord, out uint lpNumberOfCharactersRead);

    }
    class Program
    {
        const UInt32 INFINITE = 0xFFFFFFFF;
        const UInt32 WAIT_ABANDONED = 0x00000080;
        const UInt32 WAIT_OBJECT_0 = 0x00000000;
        const UInt32 WAIT_TIMEOUT = 0x00000102;

        static Mutex screenLock = new Mutex();
        static Semaphore bulletsem = new Semaphore(3, 3);
        static Thread mainThread;
        static HANDLE startevt;
        static object block = new object();

        static char[] badchar = { '-', '\\', '|', '/' };

        static int hit = 0;
        static int miss = 0;


        public static int RandomNumber(int n0, int n1)
        {
            Random newRand = new Random();
            if (n0 == 0 && n1 == 1)
            {
                return newRand.Next(0, 32767)%2;
            }
            return newRand.Next(0, 32767) % (n1 - n0) + n0;
        }
        public static void Score()
        {
            Console.Title = $"Война потоков - Попаданий: {hit}, Промахов: {miss}";
            if(miss >= 30)
            {
                lock (block)
                {
                    mainThread.Suspend();
                    Console.Title = "Игра окончена!";
                }
            }
        }
        public static string GetAt(int x, int y)
        {
            screenLock.WaitOne();
            //ConsoleHelper.WaitForSingleObject(screenLock.Handle, INFINITE);
            string result = ConsoleHelper.ReadCharacter(x, y);
            screenLock.ReleaseMutex();
            return result;
        }
        public static void WriteAt(int x, int y, char symbol)
        {
            screenLock.WaitOne();
            //Console.SetCursorPosition(x, y);
            //Console.Write(symbol);
            ConsoleHelper.WriteToBufferAt(symbol.ToString(), x, y);
            Console.SetCursorPosition(0,0);
            screenLock.ReleaseMutex();
        }
        public static void Bullet(object xy)
        {
            Point bulletPos = (Point)xy;

            if (ConsoleHelper.WaitForSingleObject(bulletsem.Handle, 0) == WAIT_TIMEOUT) return;

            while (bulletPos.Y-- > 0)
            {
                WriteAt(bulletPos.X, bulletPos.Y, '*');
                Thread.Sleep(100);
                WriteAt(bulletPos.X, bulletPos.Y, ' ');
            }
            bulletsem.Release();
        }
        public static void BadGuy(object y)
        {
            Thread myThread = Thread.CurrentThread;
            int yPos = (int)y;
            int xPos;
            int dir;
            xPos = yPos % 2 != 0 ? 0 : Console.WindowWidth;
            // установить направление в зависимости от начальной позиции
            dir = xPos == Console.WindowWidth ? -1 : 1;
            while ((dir == 1 && xPos != Console.WindowWidth) || (dir==-1 && xPos != 0))
            {
                int dly;
                bool hitMe = false;
                WriteAt(xPos, yPos, badchar[xPos % 4]);
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(40);
                    string k = GetAt(xPos, yPos);
                    if(k != "")
                    {
                        if(k[0] == '*')
                        {
                            hitMe = true;
                            break;
                        }
                    }
                }
                WriteAt(xPos, yPos, ' ');
                if (hitMe)
                {
                    Interlocked.Increment(ref hit);
                    Score();
                    myThread.Abort();
                }
                xPos += dir;
            }
            Interlocked.Increment(ref miss);
            Score();
        }
        public static void BadGuys()
        {
            ConsoleHelper.WaitForSingleObject(startevt, 15000);

            while (true)
            {
                if(RandomNumber(0,100)<(hit+miss)/ 25 + 20)
                {
                    Thread badguys = new Thread(new ParameterizedThreadStart(BadGuy));
                    badguys.Start(RandomNumber(1,10));
                }
                Thread.Sleep(1000);
            }
        }

        static void Main(string[] args)
        {
            mainThread = Thread.CurrentThread;
            startevt = ConsoleHelper.CreateEvent(startevt, true, false, null);
            int x = Console.WindowWidth / 2;
            int y = Console.WindowHeight - 1;

            Score();

            Thread badguys = new Thread(new ThreadStart(BadGuys));
            badguys.Start();

            while (true)
            {
                WriteAt(x, y, '|');

                switch (Console.ReadKey().Key)
                {
                    case ConsoleKey.LeftArrow:
                        ConsoleHelper.SetEvent(startevt);
                        WriteAt(x, y, ' ');
                        if (x > 0)
                        {
                            x--;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                        ConsoleHelper.SetEvent(startevt);
                        WriteAt(x, y, ' ');
                        if(x< Console.WindowWidth-1)
                        {
                            x++;
                        }
                        break;
                    case ConsoleKey.Spacebar:
                        Point bulletPos = new Point() { X = x, Y = y };
                        Thread myThread = new Thread(new ParameterizedThreadStart(Bullet));
                        myThread.Start(bulletPos);
                        Thread.Sleep(100);
                        break;
                }
            }

        }
    }
}
