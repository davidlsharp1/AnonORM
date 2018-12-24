using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Anon
{

        public static class AnonQuery
        {
            public static string ConnectionString = string.Empty;

            private static DataTable dataTable = new DataTable();
            public static string UpdatedSQL;
            private static List<string> paramList = new List<string>();
            private static List<string> fieldList = new List<string>();


            public static async Task<IEnumerable<dynamic>> Run(string sql)
            {
                paramList = GetParams(sql);
                DataTable dt = await GetDataTable();
                fieldList = GetFields(UpdatedSQL);
                var currentType = GetCustomType(dt);
                return ConvertToListofType(dt, currentType);
            }


            private static List<string> GetParams(string sql)
            {
                bool isParam = false;
                // split up sql and get params from it
                var paramLS = new List<string>();
                var removeLS = new List<string>();
                string param = string.Empty;

                foreach (var letter in sql)
                {
                    if (letter == 91) // [
                    {
                        isParam = true;
                        param = string.Empty;
                    }

                    if (isParam && letter == 93) // ]
                    {
                        isParam = false;
                        paramLS.Add(param);
                    }

                    if (isParam)
                    {
                        if (letter != 91)
                        {
                            param = param + letter.ToString();
                        }
                    }
                }

                foreach (var item in paramLS)
                {
                    var itemLS = item.Split('|');
                    sql = sql.Replace($"[{item}]", $"@{itemLS[0]}");
                }
                UpdatedSQL = sql;

                return paramLS;
            }


            public static async Task<DataTable> GetDataTable(CancellationToken cancellationToken = default(CancellationToken))
            {
                using (var db = new SqlConnection(ConnectionString))
                {
                    db.Open();
                    using (SqlCommand command = new SqlCommand(UpdatedSQL, db))
                    {
                        foreach (var item in paramList)
                        {
                            var pList = item.Split('|');
                            var sqlParam = new SqlParameter();
                            sqlParam.ParameterName = pList[0];
                            sqlParam.Value = pList[1];
                            command.Parameters.Add(sqlParam);
                        }

                        SqlDataReader reader;
                        try
                        {
                            reader = await command.ExecuteReaderAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            throw;
                        }
                        if (reader.HasRows)
                        {
                            dataTable.Load(reader);
                        }
                    }
                }
                return dataTable;
            }

            private static List<string> GetFields(string sql)
            {
                int start;
                int stop;

                if (sql.ToUpper().Contains("SELECT DISTINCT"))
                {
                    start = sql.ToUpper().IndexOf("SELECT DISTINCT") + 15;
                }
                else
                {
                    start = sql.ToUpper().IndexOf("SELECT") + 6;
                }

                stop = sql.ToUpper().IndexOf("FROM");
                string select = sql.Substring(start, stop - start);

                var fields = new Dictionary<string, string>();

                var tempfieldLS = select.Split(',');
                var fieldsLS = new List<string>();

                foreach (var field in tempfieldLS)
                {
                    if (field.Contains(@""""))
                    {
                        // get the name in the SQL and use that
                        var nameLS = field.Split('"');
                        fieldsLS.Add(nameLS[1].Trim());
                    }
                    else
                    {
                        if (field.Contains("."))
                        {
                            fieldsLS.Add(field.Substring(field.IndexOf(".") + 1).Trim());
                        }
                        else
                        {
                            fieldsLS.Add(field.Trim());
                        }
                    }
                }

                return fieldsLS;
            }


            private static Type GetCustomType(DataTable dataTable)
            {
                var fields = new Dictionary<string, string>();
                int counter = 0;

                foreach (var item in dataTable.Rows[0].ItemArray)
                {
                    fields.Add(fieldList[counter], item.GetType().FullName);
                    counter += 1;
                }

                var typeSignature = "MyDynamicType";
                var an = new AssemblyName(typeSignature);
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
                ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
                TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                        TypeAttributes.Public |
                        TypeAttributes.Class |
                        TypeAttributes.AutoClass |
                        TypeAttributes.AnsiClass |
                        TypeAttributes.BeforeFieldInit |
                        TypeAttributes.AutoLayout,
                        null);

                foreach (KeyValuePair<string, string> entry in fields)
                {
                    Type tempType = Type.GetType(entry.Value);
                    CreateProperty(tb, entry.Key, tempType);
                }

                //                Type dynamicType = tb.CreateType();
                Type dynamicType = tb.CreateTypeInfo();
                return dynamicType;
            }


            private static IEnumerable<dynamic> ConvertToListofType(DataTable dataTable, Type currentType)
            {
                var props = currentType.GetProperties();
                var returnLS = new List<object>();

                foreach (DataRow row in dataTable.Rows)
                {
                    var tempObject = Activator.CreateInstance(currentType);

                    foreach (DataColumn col in dataTable.Columns)
                    {
                        PropertyInfo prop = currentType.GetProperty(col.ColumnName);
                        if (row[col] != DBNull.Value)
                        {
                            prop.SetValue(tempObject, row[col]);
                        }
                    }
                    returnLS.Add(tempObject);
                }
                return ((IEnumerable<dynamic>)returnLS);
            }


            public static void CreateProperty(TypeBuilder tb, string propertyName, Type propertyType)
            {
                FieldBuilder fieldBuilder = tb.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);

                PropertyBuilder propertyBuilder = tb.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);
                MethodBuilder getPropMthdBldr = tb.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
                ILGenerator getIl = getPropMthdBldr.GetILGenerator();

                getIl.Emit(OpCodes.Ldarg_0);
                getIl.Emit(OpCodes.Ldfld, fieldBuilder);
                getIl.Emit(OpCodes.Ret);

                MethodBuilder setPropMthdBldr =
                    tb.DefineMethod("set_" + propertyName,
                      MethodAttributes.Public |
                      MethodAttributes.SpecialName |
                      MethodAttributes.HideBySig,
                      null, new[] { propertyType });

                ILGenerator setIl = setPropMthdBldr.GetILGenerator();
                Label modifyProperty = setIl.DefineLabel();
                Label exitSet = setIl.DefineLabel();

                setIl.MarkLabel(modifyProperty);
                setIl.Emit(OpCodes.Ldarg_0);
                setIl.Emit(OpCodes.Ldarg_1);
                setIl.Emit(OpCodes.Stfld, fieldBuilder);

                setIl.Emit(OpCodes.Nop);
                setIl.MarkLabel(exitSet);
                setIl.Emit(OpCodes.Ret);

                propertyBuilder.SetGetMethod(getPropMthdBldr);
                propertyBuilder.SetSetMethod(setPropMthdBldr);
            }


        }

        public static class Builder
        {
            public static Type CreateType(string sql, string connString)
            {
                int start;
                int stop;


                bool isParam = false;
                // split up sql and get params from it
                var paramLS = new List<string>();
                var removeLS = new List<string>();
                string param = string.Empty;

                foreach (var letter in sql)
                {
                    if (letter == 91) // [
                    {
                        isParam = true;
                        param = string.Empty;
                    }

                    if (isParam && letter == 93) // ]
                    {
                        isParam = false;
                        paramLS.Add(param);
                    }

                    if (isParam)
                    {
                        if (letter != 91)
                        {
                            param = param + letter.ToString();
                        }
                    }
                }

                foreach (var item in paramLS)
                {
                    var itemLS = item.Split('|');
                    sql = sql.Replace($"[{item}]", $"@{itemLS[0]}");
                }

                if (sql.ToUpper().Contains("SELECT DISTINCT"))
                {
                    start = sql.ToUpper().IndexOf("SELECT DISTINCT") + 15;
                }
                else
                {
                    start = sql.ToUpper().IndexOf("SELECT") + 6;
                }

                stop = sql.ToUpper().IndexOf("FROM");
                string select = sql.Substring(start, stop - start);

                var fields = new Dictionary<string, string>();

                var tempfieldLS = select.Split(',');
                var fieldsLS = new List<string>();

                foreach (var field in tempfieldLS)
                {
                    if (field.Contains(@""""))
                    {
                        // get the name in the SQL and use that
                        var nameLS = field.Split('"');
                        fieldsLS.Add(nameLS[1].Trim());
                    }
                    else
                    {
                        if (field.Contains("."))
                        {
                            fieldsLS.Add(field.Substring(field.IndexOf(".") + 1).Trim());
                        }
                        else
                        {
                            fieldsLS.Add(field.Trim());
                        }
                    }
                }

                int counter = 0;

                DataTable dataTable = new DataTable();

                using (var db = new SqlConnection(connString))
                {
                    db.Open();
                    using (SqlCommand command = new SqlCommand(sql, db))
                    {
                        SqlDataReader reader;
                        reader = command.ExecuteReader();
                        if (reader.HasRows)
                        {
                            dataTable.Load(reader);
                        }
                    }
                }

                foreach (var item in dataTable.Rows[0].ItemArray)
                {
                    fields.Add(fieldsLS[counter], item.GetType().FullName);
                    counter += 1;
                }

                var typeSignature = "MyDynamicType";
                var an = new AssemblyName(typeSignature);
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
                ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
                TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                        TypeAttributes.Public |
                        TypeAttributes.Class |
                        TypeAttributes.AutoClass |
                        TypeAttributes.AnsiClass |
                        TypeAttributes.BeforeFieldInit |
                        TypeAttributes.AutoLayout,
                        null);


                foreach (KeyValuePair<string, string> entry in fields)
                {
                    Type tempType = Type.GetType(entry.Value);
                    CreateProperty(tb, entry.Key, tempType);
                }

                //                Type dynamicType = tb.CreateType();
                Type dynamicType = tb.MakeGenericType();

                return dynamicType;
            }


            public static void CreateProperty(TypeBuilder tb, string propertyName, Type propertyType)
            {
                FieldBuilder fieldBuilder = tb.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);

                PropertyBuilder propertyBuilder = tb.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);
                MethodBuilder getPropMthdBldr = tb.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
                ILGenerator getIl = getPropMthdBldr.GetILGenerator();

                getIl.Emit(OpCodes.Ldarg_0);
                getIl.Emit(OpCodes.Ldfld, fieldBuilder);
                getIl.Emit(OpCodes.Ret);

                MethodBuilder setPropMthdBldr =
                    tb.DefineMethod("set_" + propertyName,
                      MethodAttributes.Public |
                      MethodAttributes.SpecialName |
                      MethodAttributes.HideBySig,
                      null, new[] { propertyType });

                ILGenerator setIl = setPropMthdBldr.GetILGenerator();
                Label modifyProperty = setIl.DefineLabel();
                Label exitSet = setIl.DefineLabel();

                setIl.MarkLabel(modifyProperty);
                setIl.Emit(OpCodes.Ldarg_0);
                setIl.Emit(OpCodes.Ldarg_1);
                setIl.Emit(OpCodes.Stfld, fieldBuilder);

                setIl.Emit(OpCodes.Nop);
                setIl.MarkLabel(exitSet);
                setIl.Emit(OpCodes.Ret);

                propertyBuilder.SetGetMethod(getPropMthdBldr);
                propertyBuilder.SetSetMethod(setPropMthdBldr);
            }
        }
}
