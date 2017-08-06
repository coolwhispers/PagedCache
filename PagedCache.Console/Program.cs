using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache.Console
{
    class Program
    {
        public class TestModel
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        static void Main(string[] args)
        {
            //var conn = new SqlConnection(
            //    "...");

            //var test = conn.ExecuteCache<TestModel>(10, "SELECT TOP (1000) Id, [Name] FROM [dbo].[Events]");

            //var count = 0;
            //foreach (var item in test)
            //{
            //    count++;
            //    System.Console.WriteLine(count + " " + item.Name);
            //}

            TryLite ty = new TryLite();

            Parallel.For(0, 10000000, x =>
            {
                if (x % 2 == 0)
                {
                    ty.Add(new TestObj { Id = Guid.NewGuid(), Name = "Test" + x });
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
