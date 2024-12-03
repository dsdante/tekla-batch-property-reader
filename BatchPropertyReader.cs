#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;
using ModelObjectSelector = Tekla.Structures.Model.UI.ModelObjectSelector;

namespace BatchPropertyReaderApp;

public class BatchPropertyReader<T> where T : ModelObject
{
    private const string header = @"
template _tmp_0
{{
    name = ""tpled_template"";
    type = TEXTUAL;
    width = 10000;
    maxheight = 100000;
    columns = (1, 1);
    gap = 0;
    fillpolicy = CONTINUOUS;
    filldirection = HORIZONTAL;
    fillstartfrom = TOPLEFT;
    margins = (0, 0, 0, 0);
    gridxspacing = 1;
    gridyspacing = 1;
    version = 4.1;

    row _tmp_1
    {{
        name = ""{0}"";
        height = {1};
        visibility = TRUE;
        usecolumns = FALSE;
        rule = """";
        contenttype = ""{0}"";
        sorttype = NONE;
";

    private const string field = @"
        valuefield _tmp_{0}
        {{
            name = ""{1}"";
            location = (0, {2});
            formula = ""GetValue(\""{1}\"")"";
            maxnumoflines = 1;
            datatype = {3};
            cacheable = TRUE;
            formatzeroasempty = FALSE;
            justify = LEFT;
            visibility = TRUE;
            angle = 0;
            length = 100;
            decimals = 16;
            sortdirection = ASCENDING;
            fontname = ""Arial Narrow"";
            fontcolor = 153;
            fonttype = 2;
            fontsize = 1;
            fontratio = 1;
            oncombine = NONE;
            aligncontenttotop = FALSE;
        }};
";

    private const string footer = @"    };
};
";

    private static readonly Dictionary<Type, string> objectTypes = new()
    {
        { typeof(Assembly), "ASSEMBLY" },
        { typeof(Part), "PART" },
        // TODO: Добавить больше типов.
    };

    public BatchPropertyContainer<T, int> Ints
    {
        get
        {
            if (newIntProperties?.Count > 0)
                Read();
            intProperties ??= [];
            return ints ??= new(intProperties);
        }
    }

    public BatchPropertyContainer<T, double> Doubles
    {
        get
        {
            if (newDoubleProperties?.Count > 0)
                Read();
            doubleProperties ??= [];
            return doubles ??= new(doubleProperties);
        }
    }

    public BatchPropertyContainer<T, string> Strings
    {
        get
        {
            if (newStringProperties?.Count > 0)
                Read();
            stringProperties ??= [];
            return strings ??= new(stringProperties);
        }
    }

    public BatchPropertyReader(IEnumerable<T> objects)
    {
        if (objects == null)
            throw new ArgumentNullException(nameof(objects));

        if (!objectTypes.ContainsKey(typeof(T)))
            throw new NotSupportedException($"Пакетное чтение атрибутов не поддерживает тип {typeof(T).Name}. Поддерживаемые типы:" + Environment.NewLine +
                string.Join(Environment.NewLine, objectTypes.Keys.Select(key => key.Name)));

        int knownCount = (objects as IReadOnlyCollection<object>)?.Count ??
                         (objects as ICollection)?.Count ??
                          0;
        this.objects = new(knownCount);
        objectsByIds = new Dictionary<int, T>(knownCount);
        foreach (var obj in objects)
        {
            this.objects.Add(obj);
            objectsByIds.Add(obj.Identifier.ID, obj);
        }
    }

    public BatchPropertyReader<T> GetIntProperties(IEnumerable<string> propertyNames)
    {
        if (propertyNames == null)
            throw new ArgumentNullException(nameof(propertyNames));

        if (newIntProperties == null)
            newIntProperties = new(propertyNames);
        else
            newIntProperties.AddRange(propertyNames);

        return this;
    }

    public BatchPropertyReader<T> GetDoubleProperties(IEnumerable<string> propertyNames)
    {
        if (propertyNames == null)
            throw new ArgumentNullException(nameof(propertyNames));

        if (newDoubleProperties == null)
            newDoubleProperties = new(propertyNames);
        else
            newDoubleProperties.AddRange(propertyNames);

        return this;
    }

    public BatchPropertyReader<T> GetStringProperties(IEnumerable<string> propertyNames)
    {
        if (propertyNames == null)
            throw new ArgumentNullException(nameof(propertyNames));

        if (newStringProperties == null)
            newStringProperties = new(propertyNames);
        else
            newStringProperties.AddRange(propertyNames);

        return this;
    }

    public BatchPropertyReader<T> GetIntProperties(params string[] propertyNames) =>
        GetIntProperties((IEnumerable<string>)propertyNames);

    public BatchPropertyReader<T> GetDoubleProperties(params string[] propertyNames) =>
        GetDoubleProperties((IEnumerable<string>)propertyNames);

    public BatchPropertyReader<T> GetStringProperties(params string[] propertyNames) =>
        GetStringProperties((IEnumerable<string>)propertyNames);

    public BatchPropertyReader<T> Read()
    {
        int newIntCount = newIntProperties?.Count ?? 0;
        int newDoubleCount = newDoubleProperties?.Count ?? 0;
        int newStringCount = newStringProperties?.Count ?? 0;
        int newTotalCount = newIntCount + newDoubleCount + newStringCount;
        if (newTotalCount == 0)
            return this;

        StringBuilder templateBuilder = new();
        templateBuilder.AppendFormat(header, objectTypes[typeof(T)], newTotalCount + 1);
        int fieldCount = 0;

        templateBuilder.AppendFormat(field, fieldCount + 2, "ID", newTotalCount - fieldCount, "INTEGER");
        fieldCount++;

        if (newIntProperties != null)
        {
            foreach (var property in newIntProperties)
            {
                templateBuilder.AppendFormat(field, fieldCount + 2, property, newTotalCount - fieldCount, "INTEGER");
                fieldCount++;
            }
            intProperties ??= [];
        }

        if (newDoubleProperties != null)
        {
            foreach (var property in newDoubleProperties)
            {
                templateBuilder.AppendFormat(field, fieldCount + 2, property, newTotalCount - fieldCount, "DOUBLE");
                fieldCount++;
            }
            doubleProperties ??= [];
        }

        if (newStringProperties != null)
        {
            foreach (var property in newStringProperties)
            {
                templateBuilder.AppendFormat(field, fieldCount + 2, property, newTotalCount - fieldCount, "STRING");
                fieldCount++;
            }
            stringProperties ??= [];
        }

        templateBuilder.Append(footer);

        var templateName = $"BatchPropertyReader_{new Random().Next():x}";
        var templateFilename = templateName + ".rpt";
        var templatePath = Path.Combine(new Model().GetInfo().ModelPath, templateFilename);
        var reportPath = Path.GetTempFileName();

        ModelObjectEnumerator.AutoFetch = true;
        var oldSelectionEnumerator = new ModelObjectSelector().GetSelectedObjects();
        ArrayList oldSelection = new(oldSelectionEnumerator.GetSize());
        foreach (var selected in oldSelectionEnumerator)
            oldSelection.Add(selected);

        ArrayList newSelection = [];
        foreach (var obj in objects)
            newSelection.Add(obj);

        File.WriteAllText(templatePath, templateBuilder.ToString());
        new ModelObjectSelector().Select(objects);
        try
        {
            try
            {
                if (!Operation.CreateReportFromSelected(templateName, reportPath, "", "", ""))
                    throw new Exception("Неизвеснаня ошибка создания отчёта.");
            }
            finally
            {
                new ModelObjectSelector().Select(oldSelection);
                File.Delete(templatePath);
            }

            var report = File.ReadLines(reportPath, Encoding.GetEncoding(1251)).GetEnumerator();
            var lines = new string[newTotalCount + 1];
            while (true)
            {
                int read = 0;
                for (; read < lines.Length && report.MoveNext(); read++)
                    lines[read] = report.Current;
                if (read < lines.Length)
                    break;

                read = 0;
                var id = objectsByIds[int.Parse(lines[read++])].Identifier;

                if (newIntProperties?.Count > 0)
                {
                    if (!intProperties!.TryGetValue(id, out var propertyDict))
                    {
                        propertyDict = [];
                        intProperties.Add(id, propertyDict);
                    }
                    foreach (var propertyName in newIntProperties)
                        if (int.TryParse(lines[read++], out var value))
                            propertyDict[propertyName] = value;
                }

                if (newDoubleProperties?.Count > 0)
                {
                    if (!doubleProperties!.TryGetValue(id, out var propertyDict))
                    {
                        propertyDict = [];
                        doubleProperties.Add(id, propertyDict);
                    }
                    foreach (var propertyName in newDoubleProperties)
                        if (double.TryParse(lines[read++], out var value))
                            propertyDict[propertyName] = value;
                }

                if (newStringProperties?.Count > 0)
                {
                    if (!stringProperties!.TryGetValue(id, out var propertyDict))
                    {
                        propertyDict = [];
                        stringProperties.Add(id, propertyDict);
                    }
                    foreach (var propertyName in newStringProperties)
                        propertyDict[propertyName] = lines[read++];
                }
            }
            newIntProperties?.Clear();
            newDoubleProperties?.Clear();
            newStringProperties?.Clear();
        }
        finally
        {
            File.Delete(reportPath);
        }

        return this;
    }

    private readonly ArrayList objects;
    private readonly Dictionary<int, T> objectsByIds;
    private List<string>? newIntProperties = null;
    private List<string>? newDoubleProperties = null;
    private List<string>? newStringProperties = null;
    private Dictionary<Identifier, Dictionary<string, int>>? intProperties = null;
    private Dictionary<Identifier, Dictionary<string, double>>? doubleProperties = null;
    private Dictionary<Identifier, Dictionary<string, string>>? stringProperties = null;
    private BatchPropertyContainer<T, int>? ints = null;
    private BatchPropertyContainer<T, double>? doubles = null;
    private BatchPropertyContainer<T, string>? strings = null;
}
