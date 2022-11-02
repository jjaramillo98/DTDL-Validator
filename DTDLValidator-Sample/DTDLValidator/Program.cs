namespace DTDLValidator
{
    using CommandLine;
    using Microsoft.Azure.DigitalTwins.Parser;
    using Microsoft.Azure.DigitalTwins.Parser.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;

    class Program
    {
        public class Options
        {
            [Option('f', "files", HelpText = "Input files to be processed. If -d option is also specified, these files are read in addition.")]
            public IEnumerable<string> InputFiles { get; set; }

            [Option('i', "interactive", Default = false, SetName = "interactive", HelpText = "Run in interactive mode")]
            public bool Interactive { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
              .WithParsed(RunOptions)
              .WithNotParsed(HandleParseError);
        }

        static void RunOptions(Options opts)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            string dtdlParserVersion = "<unknown>";
            foreach (Assembly a in assemblies)
            {
                if (a.GetName().Name.EndsWith("DigitalTwins.Parser"))
                    dtdlParserVersion = a.GetName().Version.ToString();
            }

            var modelDict = new Dictionary<string, string>();
            int count = 0;
            string lastFile = "<none>";
            try
            {
                foreach (var fi in opts.InputFiles)
                {
                    StreamReader r = new StreamReader(fi);
                    string dtdl = r.ReadToEnd();
                    r.Close();
                    modelDict.Add(fi, dtdl);
                    lastFile = fi;
                    count++;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Could not read files. \nLast file read: {lastFile}\nError: \n{e.Message}");
                return;
            }
            
            Log.Ok($"Read {count} files from specified directory");
            int errJson = 0;
            foreach (var fi in modelDict.Keys)
            {
                modelDict.TryGetValue(fi, out string dtdl);
                try
                {
                    JsonDocument.Parse(dtdl);
                }
                catch (Exception e)
                {
                    Log.Error($"Invalid json found in file {fi}.\nJson parser error \n{e.Message}");
                    errJson++;
                }
            }
            
            if (errJson > 0)
            {
                Log.Error($"\nFound  {errJson} Json parsing errors");
                return;
            }
            
            Log.Ok($"Validated JSON for all files - now validating DTDL");
            var modelList = modelDict.Values.ToList<string>();
            ModelParser parser = new ModelParser();
            parser.DtmiResolver = new DtmiResolver(Resolver);
            try
            {
                IReadOnlyDictionary<Dtmi, DTEntityInfo> om = parser.ParseAsync(modelList).GetAwaiter().GetResult();
                Log.Out("");
                Log.Ok($"**********************************************");
                Log.Ok($"** Validated all files - Your DTDL is valid **");
                Log.Ok($"**********************************************");
                Log.Out($"Found a total of {om.Keys.Count()} entities");
            }
            catch (ParsingException pe)
            {
                Log.Error($"*** Error parsing models");
                int derrcount = 1;
                foreach (ParsingError err in pe.Errors)
                {
                    Log.Error($"Error {derrcount}:");
                    Log.Error($"{err.Message}");
                    Log.Error($"Primary ID: {err.PrimaryID}");
                    Log.Error($"Secondary ID: {err.SecondaryID}");
                    Log.Error($"Property: {err.Property}\n");
                    derrcount++;
                    Environment.Exit(0);
                }
            
                return;
            }
            catch (ResolutionException)
            {
                Log.Error("Could not resolve required references");
            }
        }

        static IEnumerable<string> Resolver(IReadOnlyCollection<Dtmi> dtmis)
        {
            Log.Error($"*** Error parsing models. Missing:");
            foreach (Dtmi d in dtmis)
            {
                Log.Error($"  {d}");
            }

            return null;
        }

        static void HandleParseError(IEnumerable<Error> errs)
        {
            Log.Error($"Invalid command line.");
            foreach (Error e in errs)
            {
                Log.Error($"{e.Tag}: {e}");
            }
        }
    }
}
