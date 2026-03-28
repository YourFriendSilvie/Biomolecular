using System;
using UnityEngine;

public enum CalendarMonth
{
    January = 1,
    February = 2,
    March = 3,
    April = 4,
    May = 5,
    June = 6,
    July = 7,
    August = 8,
    September = 9,
    October = 10,
    November = 11,
    December = 12
}

public enum WorldSeason
{
    Winter,
    Spring,
    Summer,
    Autumn
}

[Serializable]
public struct MonthDay
{
    public CalendarMonth month;
    [Range(1, 31)] public int day;

    public MonthDay(CalendarMonth month, int day)
    {
        this.month = month;
        this.day = day;
    }

    public int DayOfYear => GetDayOfYear(month, day);

    public static int GetDaysInMonth(CalendarMonth month)
    {
        switch (month)
        {
            case CalendarMonth.February:
                return 28;

            case CalendarMonth.April:
            case CalendarMonth.June:
            case CalendarMonth.September:
            case CalendarMonth.November:
                return 30;

            default:
                return 31;
        }
    }

    public static int GetDayOfYear(CalendarMonth month, int day)
    {
        int total = 0;
        int clampedDay = Mathf.Clamp(day, 1, GetDaysInMonth(month));

        for (int monthIndex = 1; monthIndex < (int)month; monthIndex++)
        {
            total += GetDaysInMonth((CalendarMonth)monthIndex);
        }

        return total + clampedDay;
    }

    public override string ToString()
    {
        return $"{month} {Mathf.Clamp(day, 1, GetDaysInMonth(month))}";
    }
}

[Serializable]
public struct CalendarDate
{
    [Min(1)] public int year;
    public CalendarMonth month;
    [Range(1, 31)] public int day;

    public CalendarDate(int year, CalendarMonth month, int day)
    {
        this.year = Mathf.Max(1, year);
        this.month = month;
        this.day = Mathf.Clamp(day, 1, MonthDay.GetDaysInMonth(month));
    }

    public int DayOfYear => MonthDay.GetDayOfYear(month, day);
    public WorldSeason Season => SeasonCalendar.GetSeason(month);

    public override string ToString()
    {
        return $"{month} {Mathf.Clamp(day, 1, MonthDay.GetDaysInMonth(month))}, Year {Mathf.Max(1, year)}";
    }
}

[Serializable]
public struct CalendarRange
{
    public MonthDay start;
    public MonthDay end;

    public CalendarRange(MonthDay start, MonthDay end)
    {
        this.start = start;
        this.end = end;
    }

    public bool Contains(CalendarDate date)
    {
        int dayOfYear = date.DayOfYear;
        int startDay = start.DayOfYear;
        int endDay = end.DayOfYear;

        if (startDay <= endDay)
        {
            return dayOfYear >= startDay && dayOfYear <= endDay;
        }

        return dayOfYear >= startDay || dayOfYear <= endDay;
    }

    public float GetProgress(CalendarDate date)
    {
        if (!Contains(date))
        {
            return -1f;
        }

        int startDay = start.DayOfYear;
        int endDay = end.DayOfYear;
        int dayOfYear = date.DayOfYear;

        if (startDay == endDay)
        {
            return 1f;
        }

        if (startDay < endDay)
        {
            return Mathf.InverseLerp(startDay, endDay, dayOfYear);
        }

        int totalSpan = (SeasonCalendar.DaysPerYear - startDay) + endDay;
        if (totalSpan <= 0)
        {
            return 1f;
        }

        int offset = dayOfYear >= startDay
            ? dayOfYear - startDay
            : (SeasonCalendar.DaysPerYear - startDay) + dayOfYear;

        return Mathf.Clamp01(offset / (float)totalSpan);
    }

    public override string ToString()
    {
        return $"{start} - {end}";
    }
}

public class SeasonCalendar : MonoBehaviour
{
    public const int DaysPerYear = 365;

    public static SeasonCalendar Instance { get; private set; }

    [Header("Manual Date")]
    [SerializeField] private int year = 1;
    [SerializeField] private CalendarMonth month = CalendarMonth.June;
    [SerializeField] private int day = 15;

    public CalendarDate CurrentDate => new CalendarDate(year, month, day);
    public WorldSeason CurrentSeason => GetSeason(month);

    public static bool TryGetCurrentDate(out CalendarDate currentDate)
    {
        if (Instance != null)
        {
            currentDate = Instance.CurrentDate;
            return true;
        }

        currentDate = default;
        return false;
    }

    public static WorldSeason GetSeason(CalendarMonth month)
    {
        switch (month)
        {
            case CalendarMonth.March:
            case CalendarMonth.April:
            case CalendarMonth.May:
                return WorldSeason.Spring;

            case CalendarMonth.June:
            case CalendarMonth.July:
            case CalendarMonth.August:
                return WorldSeason.Summer;

            case CalendarMonth.September:
            case CalendarMonth.October:
            case CalendarMonth.November:
                return WorldSeason.Autumn;

            default:
                return WorldSeason.Winter;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple SeasonCalendar instances are active. Using the first one that was initialized.");
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        year = Mathf.Max(1, year);
        day = Mathf.Clamp(day, 1, MonthDay.GetDaysInMonth(month));
    }
}
