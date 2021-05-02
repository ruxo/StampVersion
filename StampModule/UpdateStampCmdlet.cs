using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml.Linq;
using RZ.Foundation;
using RZ.Foundation.Extensions;
using FilePath = System.String;
using static LanguageExt.Prelude;

namespace RZ.PowerShell.StampModule
{
    [Cmdlet(VerbsData.Update, "Stamp")]
    [OutputType(typeof(string[]))]
    public sealed class UpdateStampCmdlet : Cmdlet
    {
        [Parameter(Position = 0, Mandatory = false, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        public string? Folder{ get; set; }

        const string InitialVersion = "1.0.0.0";
        static readonly XName VersionTag = XName.Get("Version");
        static readonly XName AssemblyVersionTag = XName.Get("AssemblyVersion");
        static readonly XName FileVersionTag = XName.Get("FileVersion");

        protected override void ProcessRecord(){
            var target = Folder ?? Directory.GetCurrentDirectory();

            Directory.EnumerateFiles(target, "*.??proj")
                     .Iter(ModifyProjectFile);
        }

        void ModifyProjectFile(FilePath filepath){
            var xdoc = XDocument.Load(filepath);
            var targetFramework =
                xdoc.Descendants()
                    .Elements()
                    .Where(el => el.Name.LocalName == "TargetFramework");
            var propertyGroup =
                targetFramework.Ancestors(XName.Get("PropertyGroup"))
                               .TryFirst()
                               .Match(identity,
                                      () => throw new ApplicationException(
                                                "Cannot find the proper TargetFramework's property group!"));
            var version = GetTag(propertyGroup, VersionTag, InitialVersion);
            var assemblyVersion = GetTag(propertyGroup, AssemblyVersionTag, string.Empty);
            var fileVersion = GetTag(propertyGroup, FileVersionTag, string.Empty);

            var currentVersion = version.Value;
            var newVersion = IncreaseRevision(currentVersion);

            Console.WriteLine("{0}: {1} --> {2}", filepath, currentVersion, newVersion);

            fileVersion.Value = assemblyVersion.Value = version.Value = newVersion;

            xdoc.Save(filepath);
            WriteObject(new {filepath, newVersion, currentVersion});
        }

        static string IncreaseRevision(string version){
            var parts = version.Split('.');
            parts[3] = (int.Parse(parts[3]) + 1).ToString();
            return string.Join('.', parts);
        }

        static XElement GetTag(XElement xel, XName name, string initValue) =>
            xel.Descendants(name)
               .TryFirst()
               .Match(identity, () => new XElement(name, initValue).SideEffect(xel.Add));
    }
}
