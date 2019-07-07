using Cryptography.ECDSA;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TestECDH.Lib;

namespace TestECDH
{
    class Program
    {
        static void Main(string[] args)
        {
            //var test1 = new Test1(x => Console.WriteLine(x));
            //test1.Test1_1();
            //test1.Test1_2();
            //test1.Test1_3();



            //     var test2 = new Test2(x => Console.WriteLine(x));
            //    test2.TestSHA256();
            //     test2.TestECDSA();
            //  test2.Test2_1();
            //   test2.Test2_2();



                 var test3 = new Test3(x => Console.WriteLine(x));
                test3.GenerateCurve25519publicKeys();
        }
    }

}
