using Dcomms.DRP;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dcomms.Core.Tests
{
    [TestClass]
    public class RegistrationPublicKeyDistanceTests
    {
        [TestMethod]
        public void Test1()
        {
            Test1Subroutine(0, 0, 0);
            Test1Subroutine(65535, 0, 1);
            Test1Subroutine(65534, 0, 2);
            Test1Subroutine(65534, 1, 3);
            Test1Subroutine(32766, 32767, 1);
            Test1Subroutine(32765, 32767, 2);
            Test1Subroutine(2, 65534, 4);
            Test1Subroutine(10, 65536-10, 20);
            short correct_d = 32000;
            Test1Subroutine(10, (ushort)unchecked(10 + correct_d), correct_d);
            Test1Subroutine(32000, (ushort)unchecked(32000 + correct_d), correct_d);
            Test1Subroutine(22000, (ushort)unchecked(22000 + correct_d), correct_d);
        }
        unsafe void Test1Subroutine(ushort vector1_i, ushort vector2_i, short correct_d_i)
        {
            var d_i = RegistrationPublicKeyDistance.VectorComponentRoutine(vector1_i, vector2_i);
            Assert.IsTrue(correct_d_i == d_i);

            d_i = RegistrationPublicKeyDistance.VectorComponentRoutine(vector2_i, vector1_i);
            Assert.IsTrue(correct_d_i == d_i);
        }
    }
}
