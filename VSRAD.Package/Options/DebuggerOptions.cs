﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VSRAD.Package.DebugVisualizer;
using VSRAD.Package.Utils;

namespace VSRAD.Package.Options
{
    public sealed class DebuggerOptions : DefaultNotifyPropertyChanged
    {
        [JsonConverter(typeof(BackwardsCompatibilityWatchConverter))]
        public List<Watch> Watches { get; set; } = new List<Watch>();

        public ReadOnlyCollection<string> GetWatchSnapshot() =>
            new ReadOnlyCollection<string>(Watches.Select(w => w.Name).Distinct().ToList());

        public ReadOnlyCollection<string> GetAWatchSnapshot() =>
            new ReadOnlyCollection<string>(Watches.Where(w => w.IsAVGPR).Select(w => w.Name).Distinct().ToList());

        private uint _counter;
        public uint Counter { get => _counter; set => SetField(ref _counter, value); }

        private string _appArgs = "";
        public string AppArgs { get => _appArgs; set => SetField(ref _appArgs, value); }

        private string _breakArgs = "";
        public string BreakArgs { get => _breakArgs; set => SetField(ref _breakArgs, value); }
    }

    public sealed class BackwardsCompatibilityWatchConverter : JsonConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var watches = existingValue as List<Watch> ?? new List<Watch>();
            if (reader.TokenType != JsonToken.StartArray) return watches;

            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                if (reader.TokenType == JsonToken.String)
                    watches.Add(new Watch((string)reader.Value, VariableType.Hex, isAVGPR: false));
                else if (reader.TokenType == JsonToken.StartObject)
                    watches.Add(JObject.Load(reader).ToObject<Watch>());
            }

            return watches;
        }

        public override bool CanConvert(Type objectType) => objectType == typeof(Watch);

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
    }
}
