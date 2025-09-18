using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace Imageflow.Server.Configuration
{
    /// <summary>
    /// Represents a TOML file as an <see cref="IConfigurationSource"/>.
    /// </summary>
    public class TomlConfigurationSource : FileConfigurationSource
    {
        /// <summary>
        /// Builds the <see cref="TomlConfigurationProvider"/> for this source.
        /// </summary>
        /// <param name="builder">The <see cref="IConfigurationBuilder"/>.</param>
        /// <returns>An <see cref="TomlConfigurationProvider"/></returns>
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new TomlConfigurationProvider(this);
        }
    }

    /// <summary>
    /// A Toml file based <see cref="FileConfigurationProvider"/>.
    /// </summary>
    public class TomlConfigurationProvider : FileConfigurationProvider
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="source">The <see cref="TomlConfigurationSource"/>.</param>
        public TomlConfigurationProvider(TomlConfigurationSource source) : base(source) { }

        /// <summary>
        /// Loads Toml configuration key/values from a stream into a provider.
        /// </summary>
        /// <param name="stream">The toml <see cref="Stream"/> to load configuration data from.</param>
        public override void Load(Stream stream)
        {
            Data = TomlConfigurationFileParser.Parse(stream);
        }
    }

    internal static class TomlConfigurationFileParser
    {
        public static IDictionary<string, string?> Parse(Stream input)
        {
            var data = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StreamReader(input);
            var toml = reader.ReadToEnd();
            var model = Toml.ToModel(toml);
            Visit(model, data, new Stack<string>());
            return data;
        }

        private static void Visit(TomlTable table, IDictionary<string, string?> data, Stack<string> context)
        {
            foreach (var (key, value) in table)
            {
                context.Push(key);
                VisitValue(value, data, context);
                context.Pop();
            }
        }

        private static void VisitValue(object? value, IDictionary<string, string?> data, Stack<string> context)
        {
            if (value is null) { return; }

            var currentPath = ConfigurationPath.Combine(context.Reverse());
            if (value is TomlTable table)
            {
                Visit(table, data, context);
            } else if (value is TomlTableArray tableArray)
            {
                for (int i = 0; i < tableArray.Count; i++)
                {
                    context.Push(i.ToString(CultureInfo.InvariantCulture));
                    Visit(tableArray[i], data, context);
                    context.Pop();
                }
            } else if (value is TomlArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    context.Push(i.ToString(CultureInfo.InvariantCulture));
                    VisitValue(array[i], data, context);
                    context.Pop();
                }
            } else if (value is string s)
            {
                data[currentPath] = s;
            } else if (value is bool b)
            {
                data[currentPath] = b.ToString().ToLowerInvariant();
            } else if (value is long l)
            {
                data[currentPath] = l.ToString(CultureInfo.InvariantCulture);
            } else if (value is double d)
            {
                data[currentPath] = d.ToString(CultureInfo.InvariantCulture);
            } else if (value is TomlDateTime dt)
            {
                data[currentPath] = dt.ToString();
            }
        }
    }
}



