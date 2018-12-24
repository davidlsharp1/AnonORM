using System;

namespace ExampleConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Anon ORM demo!");

            Anon.AnonQuery.ConnectionString = dbSettings.ConnectionString;

            var isComplete = 0;

            var SQL = $@"select p.FirstName, t.Detail
                            from Person p, Todo t
                            where p.ID = t.PersonID
                            and t.Complete = [complete|{isComplete}]";

            var results = Anon.AnonQuery.Run(SQL);


            foreach (var item in results.Result)
            {
                Console.WriteLine($"{item.FirstName} : {item.Detail}");
            }

            Console.ReadKey();
        }
    }
}
