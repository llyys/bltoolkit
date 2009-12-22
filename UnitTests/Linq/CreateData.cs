﻿using System;
using System.IO;

using NUnit.Framework;

using BLToolkit.Data.DataProvider;
using BLToolkit.DataAccess;

using Data.Linq;
using Data.Linq.Model;

namespace Create
{
	[TestFixture]
	public class CreateData : TestBase
	{
		static void RunScript(string configString, string divider, string name)
		{
			Console.WriteLine("=== " + name + " === \n");

			var text = File.ReadAllText(@"..\..\..\..\Data\Create Scripts\" + name + ".sql");

			var idx = text.IndexOf("SKIP " + configString + " BEGIN");

			if (idx >= 0)
				text = text.Substring(0, idx) + text.Substring(text.IndexOf("SKIP " + configString + " END"));

			var cmds = text.Replace("\r", "").Replace(divider, "\x1").Split('\x1');

			Exception exception = null;

			using (var db = new TestDbManager(configString))
			{
				foreach (var cmd in cmds)
				{
					var command = cmd.Trim();

					if (command.Length == 0)
						continue;

					try 
					{
						Console.WriteLine(command);
						db.SetCommand(command).ExecuteNonQuery();
						Console.WriteLine("\nOK\n");
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
						Console.WriteLine("\nFAILED\n");

						if (exception == null)
							exception = ex;
					}
				}

				if (exception != null)
					throw exception;

				new SqlQuery<LinqDataTypes>().Insert(db, new[]
				{
					new LinqDataTypes { ID =  1, MoneyValue =  1.11m, DateTimeValue = new DateTime(2001, 1, 11, 1, 11, 21, 100), BoolValue = true,  GuidValue = new Guid("EF129165-6FFE-4DF9-BB6B-BB16E413C883") },
					new LinqDataTypes { ID =  2, MoneyValue =  2.22m, DateTimeValue = new DateTime(2005, 5, 15, 5, 15, 25, 500), BoolValue = false, GuidValue = new Guid("BC663A61-7B40-4681-AC38-F9AAF55B706B") },
					new LinqDataTypes { ID =  3, MoneyValue =  3.33m, DateTimeValue = new DateTime(2009, 9, 19, 9, 19, 29,  90), BoolValue = true,  GuidValue = new Guid("D2F970C0-35AC-4987-9CD5-5BADB1757436") },
					new LinqDataTypes { ID =  4, MoneyValue =  4.33m, DateTimeValue = new DateTime(2009, 9, 20, 9, 19, 29,  90), BoolValue = false, GuidValue = new Guid("40932FDB-1543-4E4A-AC2C-CA371604FB4B") },
					new LinqDataTypes { ID =  5, MoneyValue =  5.33m, DateTimeValue = new DateTime(2009, 9, 21, 9, 19, 29,  90), BoolValue = true,  GuidValue = new Guid("FEBE3ECA-CB5F-40B2-AD39-2979D312AFCA") },
					new LinqDataTypes { ID =  6, MoneyValue =  6.33m, DateTimeValue = new DateTime(2009, 9, 22, 9, 19, 29,  90), BoolValue = false, GuidValue = new Guid("8D3C5D1D-47DB-4730-9FE7-968F6228A4C0") },
					new LinqDataTypes { ID =  7, MoneyValue =  7.33m, DateTimeValue = new DateTime(2009, 9, 23, 9, 19, 29,  90), BoolValue = true,  GuidValue = new Guid("48094115-83AF-46DD-A906-BFF26EE21EE2") },
					new LinqDataTypes { ID =  8, MoneyValue =  8.33m, DateTimeValue = new DateTime(2009, 9, 24, 9, 19, 29,  90), BoolValue = false, GuidValue = new Guid("C1139F1F-1335-4CD4-937E-92602F732DD3") },
					new LinqDataTypes { ID =  9, MoneyValue =  9.33m, DateTimeValue = new DateTime(2009, 9, 25, 9, 19, 29,  90), BoolValue = true,  GuidValue = new Guid("46C5C512-3D4B-4CF7-B4E7-1DE080789E5D") },
					new LinqDataTypes { ID = 10, MoneyValue = 10.33m, DateTimeValue = new DateTime(2009, 9, 26, 9, 19, 29,  90), BoolValue = false, GuidValue = new Guid("61B2BC55-147F-4B40-93ED-A4AA83602FEE") },
					new LinqDataTypes { ID = 11, MoneyValue = 11.33m, DateTimeValue = new DateTime(2009, 9, 27, 9, 19, 29,  90), BoolValue = true,  GuidValue = new Guid("D3021D18-97F0-4DC0-98D0-F0C7DF4A1230") },
				});

				new SqlQuery<Parent>().Insert(db, new[]
				{
					new Parent { ParentID = 1, Value1 = 1    },
					new Parent { ParentID = 2, Value1 = null },
					new Parent { ParentID = 3, Value1 = 3    },
					new Parent { ParentID = 4, Value1 = null },
					new Parent { ParentID = 5, Value1 = 5    },
					new Parent { ParentID = 6, Value1 = 6    },
				});

				new SqlQuery<Child>().Insert(db, new[]
				{
					new Child { ParentID = 1, ChildID = 11 },
					new Child { ParentID = 2, ChildID = 21 },
					new Child { ParentID = 2, ChildID = 22 },
					new Child { ParentID = 3, ChildID = 31 },
					new Child { ParentID = 3, ChildID = 32 },
					new Child { ParentID = 3, ChildID = 33 },
					new Child { ParentID = 4, ChildID = 41 },
					new Child { ParentID = 4, ChildID = 42 },
					new Child { ParentID = 4, ChildID = 43 },
					new Child { ParentID = 4, ChildID = 44 },
					new Child { ParentID = 6, ChildID = 61 },
					new Child { ParentID = 6, ChildID = 62 },
					new Child { ParentID = 6, ChildID = 63 },
					new Child { ParentID = 6, ChildID = 64 },
					new Child { ParentID = 6, ChildID = 65 },
					new Child { ParentID = 6, ChildID = 66 },
				});

				new SqlQuery<GrandChild>().Insert(db, new[]
				{
					new GrandChild { ParentID = 1, ChildID = 11, GrandChildID = 111 },
					new GrandChild { ParentID = 2, ChildID = 21, GrandChildID = 211 },
					new GrandChild { ParentID = 2, ChildID = 21, GrandChildID = 212 },
					new GrandChild { ParentID = 2, ChildID = 22, GrandChildID = 221 },
					new GrandChild { ParentID = 2, ChildID = 22, GrandChildID = 222 },
					new GrandChild { ParentID = 3, ChildID = 31, GrandChildID = 311 },
					new GrandChild { ParentID = 3, ChildID = 31, GrandChildID = 312 },
					new GrandChild { ParentID = 3, ChildID = 31, GrandChildID = 313 },
					new GrandChild { ParentID = 3, ChildID = 32, GrandChildID = 321 },
					new GrandChild { ParentID = 3, ChildID = 32, GrandChildID = 322 },
					new GrandChild { ParentID = 3, ChildID = 32, GrandChildID = 323 },
					new GrandChild { ParentID = 3, ChildID = 33, GrandChildID = 331 },
					new GrandChild { ParentID = 3, ChildID = 33, GrandChildID = 332 },
					new GrandChild { ParentID = 3, ChildID = 33, GrandChildID = 333 },
					new GrandChild { ParentID = 4, ChildID = 41, GrandChildID = 411 },
					new GrandChild { ParentID = 4, ChildID = 41, GrandChildID = 412 },
					new GrandChild { ParentID = 4, ChildID = 41, GrandChildID = 413 },
					new GrandChild { ParentID = 4, ChildID = 41, GrandChildID = 414 },
					new GrandChild { ParentID = 4, ChildID = 42, GrandChildID = 421 },
					new GrandChild { ParentID = 4, ChildID = 42, GrandChildID = 422 },
					new GrandChild { ParentID = 4, ChildID = 42, GrandChildID = 423 },
					new GrandChild { ParentID = 4, ChildID = 42, GrandChildID = 424 },
				});
			}
		}

		[Test] public void DB2       () { RunScript(ProviderName.DB2,        "\nGO\n",  "DB2");        }
		[Test] public void Informix  () { RunScript(ProviderName.Informix,   "\nGO\n",  "Informix");   }
		[Test] public void Oracle    () { RunScript("Oracle",                "\n/\n",   "Oracle");     }
		[Test] public void Firebird  () { RunScript(ProviderName.Firebird,   "COMMIT;", "Firebird2");  }
		[Test] public void PostgreSQL() { RunScript(ProviderName.PostgreSQL, "\nGO\n",  "PostgreSQL"); }
		[Test] public void MySql     () { RunScript(ProviderName.MySql,      "\nGO\n",  "MySql");      }
		[Test] public void Sql2008   () { RunScript("Sql2008",               "\nGO\n",  "MsSql");      }
		[Test] public void Sql2005   () { RunScript("Sql2005",               "\nGO\n",  "MsSql");      }
		[Test] public void SqlCe     () { RunScript(ProviderName.SqlCe,      "\nGO\n",  "SqlCe");      }
		[Test] public void Sybase    () { RunScript(ProviderName.Sybase,     "\nGO\n",  "Sybase");     }
		[Test] public void SQLite    () { RunScript(ProviderName.SQLite,     "\nGO\n",  "SQLite");     }
		[Test] public void Access    () { RunScript(ProviderName.Access,     "\nGO\n",  "Access");     }
	}
}
