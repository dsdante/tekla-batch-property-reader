#nullable enable
using System;
using System.Collections.Generic;
using Tekla.Structures;
using Tekla.Structures.Datatype;
using Tekla.Structures.Model;

namespace BatchPropertyReaderApp;

public class BatchPropertyContainer<TTeklaObject, TData> where TTeklaObject : ModelObject
{
    public TData? this[TTeklaObject modelObject, string propertyName]
    {
        get
        {
            if (modelObject == null)
                throw new ArgumentNullException(nameof(modelObject));
            if (propertyName == null)
                throw new ArgumentNullException(nameof(propertyName));

            if (data.TryGetValue(modelObject.Identifier, out var objectDict) &&
                objectDict.TryGetValue(propertyName, out var result))
            {
                return result;
            }

            if (typeof(TData) == typeof(int) || typeof(TData) == typeof(int))
                return (TData)(object)Constants.XS_DEFAULT;
            return default;
        }
    }

    private readonly Dictionary<Identifier, Dictionary<string, TData>> data;

    internal BatchPropertyContainer(Dictionary<Identifier, Dictionary<string, TData>> data) =>
        this.data = data;
}
