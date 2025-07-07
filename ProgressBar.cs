using System;

namespace StorageSphere
{
    public class ProgressBar : IDisposable
    {
        private readonly int _total;
        private int _current;
        private int _lastPercent = -1;

        public ProgressBar(int total)
        {
            _total = total;
        }

        public void Report(int value)
        {
            _current = value;
            int percent = (int)((double)_current / _total * 100);
            if (percent != _lastPercent)
            {
                Console.Write($"\r[{new string('#', percent / 2)}{new string(' ', 50 - percent / 2)}] {percent}%");
                _lastPercent = percent;
            }
        }

        public void Dispose()
        {
            Console.WriteLine();
        }
    }
}