// Copyright (c) Imazen LLC.
// No part of this project, including this file, may be copied, modified,
// propagated, or distributed except as permitted in COPYRIGHT.txt.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Imageflow.Server.Storage.AzureBlob;
using Imageflow.Server.Storage.RemoteReader;
using Imageflow.Server.Storage.S3;
using Imazen.Common.Issues;
using PublicApiGenerator;
using Xunit;

namespace Imageflow.Server.Tests
{
    public class ApiSurface
    {

        private string GetApiTextDir()
        {
            string codeBase = null;
            try
            {
                codeBase = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
            }
            catch { }

            var searchLocations = new[] { Assembly.GetExecutingAssembly().Location, typeof(Imazen.Common.Issues.Issue).Assembly.Location, codeBase };
            foreach(var location in searchLocations)
            {
                var attempt = location != null ? FindSolutionDir(Path.GetDirectoryName(location), "Imageflow.Server.sln") : null;
                if (attempt != null) return Path.Combine(attempt, "tests", "api-surface");
            }
            return null;
        }

        private string FindSolutionDir(string startDir, string filename)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, filename)))
                {
                    return dir;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) return null;
                dir = parent;
            }
            return null;
        }

        [Fact]
        public void GenerateApiSurfaceText()
        {
            var dir = GetApiTextDir();
            if (dir == null) return; // We can do nothing
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var types = new[]
            {
                typeof(ImageflowMiddleware),
                typeof(Imazen.Common.Issues.Issue),
                typeof(Imazen.HybridCache.HybridCache),
                typeof(S3Service),
                typeof(RemoteReaderService),
                typeof(AzureBlobService),

            };

            foreach (var t in types)
            {
                var assembly = t.Assembly;
                var assemblyName = assembly.GetName().Name;
                var apiText = assembly.GeneratePublicApi(new ApiGeneratorOptions());

                apiText = new Regex("Imazen.Common.Licensing.BuildDate\\(\"[^\"]*\"\\)").Replace(apiText,
                    "Imazen.Common.Licensing.BuildDate(\"[removed]\")");

                apiText = new Regex("Imazen.Common.Licensing.Commit\\(\"[^\"]*\"\\)").Replace(apiText,
                    "Imazen.Common.Licensing.Commit(\"[removed]\")");


                var fileName = Path.Combine(dir, assemblyName + ".txt");
                File.WriteAllText(fileName, apiText);
            }
        }
    }
}
