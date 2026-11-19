using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Education2k
{
    public static class AppInfo
    {
        public const string Name = "החתול הסקרן";
        public const string Slogan = "למידה מתוך סקרנות";
        public static readonly string Version;
        public static readonly DateTimeOffset LastUpdated;

        static AppInfo()
        {
            var assembly = typeof(AppInfo).Assembly;
            var version = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).Cast<AssemblyInformationalVersionAttribute>().SingleOrDefault();
            var updated = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false).Cast<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "VersionDate");
            if (version == null || updated == null) throw new InvalidOperationException("חסר מידע על גרסת התוכנה. יש לקמפל מחדש באמצעות build.ps1.");
            Version = version.InformationalVersion;
            LastUpdated = DateTimeOffset.ParseExact(updated.Value, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None);
        }

        public static string VersionLabel
        {
            get { return "גרסה \u200e" + Version + "\u200e · עדכון אחרון: \u200e" + LastUpdated.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + "\u200e"; }
        }
    }
}
