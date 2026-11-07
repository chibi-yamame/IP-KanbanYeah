using System;
using System.Collections.Generic;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// Finding InterPot3.jar from the uninstall entries (docs/design/fullcolor.md「InterPot3.jar の場所」) without touching the
    /// registry, and the shortcut's name.
    /// </summary>
    public class GameLauncherTests
    {
        static (string, string) E(string name, string location) => (name, location);

        static Func<string, bool> Existing(params string[] files)
        {
            var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        [Fact]
        public void MatchesDisplayNameIgnoringCase()
        {
            var exists = Existing(@"D:\Games\IP\InterPot3.jar");
            Assert.Equal(@"D:\Games\IP\InterPot3.jar",
                GameLauncher.PickInstallLocation(new[] { E("interpot3", @"D:\Games\IP") }, exists));
            Assert.Equal(@"D:\Games\IP\InterPot3.jar",
                GameLauncher.PickInstallLocation(new[] { E("INTERPOT3", @"D:\Games\IP") }, exists));
        }

        [Fact]
        public void DisplayNameMustMatchWholly()
        {
            var exists = Existing(@"D:\Games\IP\InterPot3.jar");
            Assert.Null(GameLauncher.PickInstallLocation(new[]
            {
                E("InterPot3 Beta", @"D:\Games\IP"),
                E("InterPot", @"D:\Games\IP"),
                E("Other", @"D:\Games\IP"),
                E(null, @"D:\Games\IP"),
            }, exists));
        }

        [Theory]
        [InlineData(@"""D:\Games\IP""")]
        [InlineData(@"D:\Games\IP\")]
        [InlineData(@"""D:\Games\IP\""")]
        [InlineData(@" D:\Games\IP\\ ")]
        public void TrimsQuotesAndTrailingBackslash(string location)
        {
            Assert.Equal(@"D:\Games\IP\InterPot3.jar",
                GameLauncher.PickInstallLocation(new[] { E("InterPot3", location) }, Existing(@"D:\Games\IP\InterPot3.jar")));
        }

        [Fact]
        public void FirstExistingWins()
        {
            var entries = new[]
            {
                E("InterPot3", @"C:\Old\InterPot3"),   // uninstalled, the jar is gone
                E("InterPot3", ""),
                E("InterPot3", null),
                E("InterPot3", @"D:\New\InterPot3"),
                E("InterPot3", @"E:\Later\InterPot3"),
            };
            var exists = Existing(@"D:\New\InterPot3\InterPot3.jar", @"E:\Later\InterPot3\InterPot3.jar");
            Assert.Equal(@"D:\New\InterPot3\InterPot3.jar", GameLauncher.PickInstallLocation(entries, exists));
        }

        [Fact]
        public void NoneIsNull()
        {
            Assert.Null(GameLauncher.PickInstallLocation(new (string, string)[0], _ => true));
            Assert.Null(GameLauncher.PickInstallLocation(new[] { E("InterPot3", @"D:\Games\IP") }, _ => false));
        }

        [Fact]
        public void InvalidLocationIsSkipped()
        {
            var entries = new[] { E("InterPot3", "D:\\Bad|Path"), E("InterPot3", @"D:\Games\IP") };
            Assert.Equal(@"D:\Games\IP\InterPot3.jar",
                GameLauncher.PickInstallLocation(entries, Existing(@"D:\Games\IP\InterPot3.jar")));
        }

        [Fact]
        public void ShortcutName() => Assert.Equal("InterPot3 (屋).lnk", GameLauncher.ShortcutName);
    }
}
