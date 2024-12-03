#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tekla.Structures;
using Tekla.Structures.Datatype;
using Tekla.Structures.Filtering;
using Tekla.Structures.Filtering.Categories;
using Tekla.Structures.Model;

namespace BatchPropertyReaderApp;

internal static class Program
{
    static void Main()
    {
        var filter = new BinaryFilterExpressionCollection
        {
            new BinaryFilterExpressionItem(
                new BinaryFilterExpression(
                    new ObjectFilterExpressions.Type(),
                    NumericOperatorType.IS_EQUAL,
                    new NumericConstantFilterExpression(TeklaStructuresDatabaseTypeEnum.ASSEMBLY)),
                BinaryFilterOperatorType.BOOLEAN_AND),
            new BinaryFilterExpressionItem(
                new BinaryFilterExpression(
                    new TemplateFilterExpressions.CustomString("PREFIX"),
                    StringOperatorType.CONTAINS,
                    new StringConstantFilterExpression("")
                )
            )
        };

        ModelObjectEnumerator.AutoFetch = true;
        var assemblies = new Model()
            .GetModelObjectSelector()
            .GetObjectsByFilter(filter)
            .AsEnumerable<Assembly>()
            .ToList();

        GetAllUserProperties(assemblies);  // ~1.3s
        //TestBatchPropertyReader(assemblies);  //~3.5s
    }

    static void GetAllUserProperties(List<Assembly> assemblies)
    {
        var lines = new string[assemblies.Count];
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(Enumerable.Range(0, Environment.ProcessorCount), threadIndex =>
        {
            Hashtable properties = [];
            for (int i = assemblies.Count * threadIndex / Environment.ProcessorCount;
                 i < assemblies.Count * (threadIndex + 1) / Environment.ProcessorCount;
                 i++)
            {
                var id = assemblies[i].Identifier.ID;
                assemblies[i].GetAllUserProperties(ref properties);
                var line = $"{id};{properties["ais_kks"]};{properties["ais_kks_parent"]};{properties["ais_kks_set"]};{properties["ais_kks_prefix"]};{properties["ais_kks_number"]};{properties["ais_kks_parent_guid"]};{properties["ais_object_type_ass"]}";
                if (line.Length > 6)
                    lines[i] = line;
                properties.Clear();
            }
        });
        Console.WriteLine($"GetAllUserProperties: {sw.Elapsed}");
        File.AppendAllText("BatchPropertyReaderApp.log", $"GetAllUserProperties: {sw.Elapsed}" + Environment.NewLine);

        var csvLines = lines
            .Where(line => line.Length > 6)
            .OrderBy(line => line);

        File.WriteAllText("out_get_all.csv", "ID;ais_kks;ais_kks_parent;ais_kks_set;ais_kks_prefix;ais_kks_number;ais_kks_parent_guid;ais_object_type_ass" + Environment.NewLine);
        File.AppendAllLines("out_get_all.csv", lines.Where(line => line != null));
    }

    static void TestBatchPropertyReader(List<Assembly> assemblies)
    {
        var sw = Stopwatch.StartNew();
        var propertyReader = new BatchPropertyReader<Assembly>(assemblies)
            .GetIntProperties("ais_kks_number")
            .GetStringProperties(
                "ais_kks",
                "ais_kks_parent",
                "ais_kks_set",
                "ais_kks_prefix",
                "ais_kks_number",
                "ais_kks_parent_guid",
                "ais_object_type_ass")
            .Read();
        Console.WriteLine($"BatchPropertyReader: {sw.Elapsed}");
        File.AppendAllText("BatchPropertyReaderApp.log", $"BatchPropertyReader: {sw.Elapsed}" + Environment.NewLine);

        var ints = propertyReader.Ints;
        var strings = propertyReader.Strings;
        var csvLines = assemblies
            .Select(asm =>
            {
                var id = asm.Identifier.ID;
                var kks = strings[asm, "ais_kks"];
                var parent = strings[asm, "ais_kks_parent"];
                var set = strings[asm, "ais_kks_set"];
                var prefix = strings[asm, "ais_kks_prefix"];
                var parentGuid = strings[asm, "ais_kks_parent_guid"];
                var ass = strings[asm, "ais_object_type_ass"];
                var number = ints[asm, "ais_kks_number"];
                var numberString = number == Constants.XS_DEFAULT ? "" : number.ToString();
                return $"{id};{kks};{parent};{set};{prefix};{numberString};{parentGuid};{ass}";
            })
            .Where(line => line.Length > 6)
            .OrderBy(line => line);

        File.WriteAllText("out_batch.csv", "ID;ais_kks;ais_kks_parent;ais_kks_set;ais_kks_prefix;ais_kks_number;ais_kks_parent_guid;ais_object_type_ass" + Environment.NewLine);
        File.AppendAllLines("out_batch.csv", csvLines);
    }

    static IEnumerable<T> AsEnumerable<T>(this IEnumerator enumerator)
    {
        while (enumerator.MoveNext())
            yield return (T)enumerator.Current;
    }
}
