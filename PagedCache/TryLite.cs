using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache
{
    public class TestObj
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class TryLite
    {
        private static LiteDB.LiteDatabase db = new LiteDB.LiteDatabase(new System.IO.MemoryStream());

        private Guid id = Guid.NewGuid();

        public void Add(TestObj obj)
        {
            var customers = db.GetCollection<TestObj>(id.ToString("N"));
            customers.Insert(obj);

        }

        public IEnumerable<TestObj> Get()
        {
            var customers = db.GetCollection<TestObj>(id.ToString("N"));
            
            return customers.FindAll();
        }

    }
}
