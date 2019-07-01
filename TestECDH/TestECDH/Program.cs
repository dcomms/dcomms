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
            var test1 = new Test1(x => Console.WriteLine(x));
            test1.Test1_1();
            test1.Test1_2();
            test1.Test1_3();
        }
    }

}
