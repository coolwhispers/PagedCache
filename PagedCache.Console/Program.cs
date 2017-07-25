using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache.Console
{
    class Program
    {
        static void Main(string[] args)
        {

            TryLite ty = new TryLite();

            Parallel.For(0, 10000000, x =>
            {
                if (x % 2 == 0)
                {
                    ty.Add(new TestObj { Id = Convert.ToString(x), Name = "Test" + x });
                }
                else
                {
                    var datax = ty.Get();
                }

            });

            var data = ty.Get();
            foreach (var d in data)
            {
                System.Console.WriteLine(d.Id);
            }
            //System.Console.Write(System.AppDomain.CurrentDomain.BaseDirectory);

            System.Console.Read();
        }
    }
}
