# AnonORM
SQL Server ORM for simple cases where you just want to write a SQL query without creating model to represent the results.  AnonORM derives the "model" in memory for the results from the data.  This allows you iterate, etc but you dont get any intellisense with the objects.

This means I can create a simple query that is joining two tables and use the results directly without creating a model with properties for each of the fields to represent the data.

This was a just a test case and I was curious to see if this could be practical.

```
select p.FirstName, t.Detail
from Person p, Todo t
where p.ID = t.PersonID
and t.Complete = 0
```

Now in my code I can do:

```
var results = Anon.AnonQuery.Run(SQL);


foreach (var item in results.Result)  // the code is async so I just do a .Result for a demo
{
    Console.WriteLine($"{item.FirstName} : {item.Detail}");
}
```            

Note in a typical situation you would NOT use a ".Result" after the query results.  Thats just for this console demo.
