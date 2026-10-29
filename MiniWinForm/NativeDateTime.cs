using Windows.Win32.Foundation;

namespace MiniWinForm;

internal static class NativeDateTime
{
    internal static SYSTEMTIME ToSystemTime(DateTime value)
    {
        return new SYSTEMTIME
        {
            wYear = (ushort)value.Year,
            wMonth = (ushort)value.Month,
            wDayOfWeek = (ushort)value.DayOfWeek,
            wDay = (ushort)value.Day,
            wHour = (ushort)value.Hour,
            wMinute = (ushort)value.Minute,
            wSecond = (ushort)value.Second,
            wMilliseconds = (ushort)value.Millisecond,
        };
    }

    internal static DateTime FromSystemTime(SYSTEMTIME value)
    {
        if (value.wYear < 1 || value.wMonth is < 1 or > 12 || value.wDay < 1)
        {
            return DateTime.Now;
        }

        return new DateTime(
            value.wYear,
            value.wMonth,
            value.wDay,
            value.wHour,
            value.wMinute,
            value.wSecond,
            value.wMilliseconds);
    }
}
