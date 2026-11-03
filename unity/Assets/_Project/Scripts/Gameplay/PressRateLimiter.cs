using System;

namespace MM.Gameplay
{
    // Anti-macro limit: at most N accepted presses inside any sliding 1-second window.
    // Plain C# with the clock passed in, so it can be covered by EditMode tests later.
    public sealed class PressRateLimiter
    {
        const double k_WindowSeconds = 1.0;

        // Ring buffer of the last N accepted press times; once full, m_Next points at the oldest.
        readonly double[] m_Times;
        int m_Next;
        int m_Count;

        public PressRateLimiter(int maxPerSecond)
        {
            m_Times = new double[Math.Max(1, maxPerSecond)];
        }

        public bool TryAccept(double now)
        {
            if (m_Count == m_Times.Length)
            {
                if (now - m_Times[m_Next] < k_WindowSeconds)
                    return false;
            }
            else
            {
                m_Count++;
            }

            m_Times[m_Next] = now;
            m_Next = (m_Next + 1) % m_Times.Length;
            return true;
        }

        public void Reset()
        {
            m_Next = 0;
            m_Count = 0;
        }
    }
}
