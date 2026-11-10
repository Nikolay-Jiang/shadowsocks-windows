using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Controller;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using Shadowsocks.Controller.Hotkeys;
using System.Diagnostics;


namespace Shadowsocks.Test
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public void TestHotKey2Str()
        {
            Assert.AreEqual("Ctrl+A", HotKeys.HotKey2Str(Keys.A, Keys.Control));
            Assert.AreEqual("Ctrl+Alt+D2", HotKeys.HotKey2Str(Keys.D2, (Keys.Alt | Keys.Control)));
            Assert.AreEqual("Ctrl+Alt+Shift+NumPad7", HotKeys.HotKey2Str(Keys.NumPad7, (Keys.Alt | Keys.Control | Keys.Shift)));
            Assert.AreEqual("Ctrl+Alt+Shift+F6", HotKeys.HotKey2Str(Keys.F6, (Keys.Alt | Keys.Control | Keys.Shift)));
            Assert.AreNotEqual("Ctrl+Shift+Alt+F6", HotKeys.HotKey2Str(Keys.F6, (Keys.Alt | Keys.Control | Keys.Shift)));
        }

        [TestMethod]
        public void TestStr2HotKey()
        {
            Assert.IsTrue(HotKeys.Str2HotKey("Ctrl+A").Equals(new HotKey(Keys.A, Keys.Control)));
            Assert.IsTrue(HotKeys.Str2HotKey("Ctrl+Alt+A").Equals(new HotKey(Keys.A, (Keys.Control | Keys.Alt))));
            Assert.IsTrue(HotKeys.Str2HotKey("Ctrl+Shift+A").Equals(new HotKey(Keys.A, (Keys.Control | Keys.Shift))));
            Assert.IsTrue(HotKeys.Str2HotKey("Ctrl+Alt+Shift+A").Equals(new HotKey(Keys.A, (Keys.Control | Keys.Alt | Keys.Shift))));
            HotKey testKey0 = HotKeys.Str2HotKey("Ctrl+Alt+Shift+A");
            Assert.IsTrue(testKey0 != null && testKey0.Equals(new HotKey(Keys.A, (Keys.Control | Keys.Alt | Keys.Shift))));
            HotKey testKey1 = HotKeys.Str2HotKey("Ctrl+Alt+Shift+F2");
            Assert.IsTrue(testKey1 != null && testKey1.Equals(new HotKey(Keys.F2, (Keys.Control | Keys.Alt | Keys.Shift))));
            HotKey testKey2 = HotKeys.Str2HotKey("Ctrl+Shift+Alt+D7");
            Assert.IsTrue(testKey2 != null && testKey2.Equals(new HotKey(Keys.D7, (Keys.Control | Keys.Alt | Keys.Shift))));
            HotKey testKey3 = HotKeys.Str2HotKey("Ctrl+Shift+Alt+NumPad7");
            Assert.IsTrue(testKey3 != null && testKey3.Equals(new HotKey(Keys.NumPad7, (Keys.Control | Keys.Alt | Keys.Shift))));
        }

    }
}
