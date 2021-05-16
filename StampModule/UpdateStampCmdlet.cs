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
    public sealed class UpdateStampCmdlet : PSCmdlet
    {
        const string FullRevision = "FullRevision";
        const string RevisionOnly = "RevisionOnly";
        const string NewMinor = "NewMinor";
        const string NewMajor = "NewMajor";

        [Parameter(Position = 0, Mandatory = false, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        public FilePath? Folder{ get; set; }

        [Parameter(Position = 1)]
        [ValidateSet(FullRevision, RevisionOnly, NewMinor, NewMajor)]
        [PSDefaultValue(Value = FullRevision, Help = "Increase Minor and Revision")]
        public string Strategy{ get; set; } = FullRevision;

        [Parameter(Position = 2)]
        public bool ResetBuild{ get; set; }

        const string InitialVersion = "1.0.0.0";
        static readonly XName VersionTag = XName.Get("Version");
        static readonly XName AssemblyVersionTag = XName.Get("AssemblyVersion");
        static readonly XName FileVersionTag = XName.Get("FileVersion");

        protected override void ProcessRecord() {
            Environment.CurrentDirectory = SessionState.Path.CurrentLocation.Path;
            var target = Path.GetFullPath(Folder ?? "./");

            Directory.EnumerateFiles(target, "*.??proj", SearchOption.AllDirectories).Iter(ModifyProjectFile);
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
                               .Match(identity, () => throw new ApplicationException("Cannot find the proper TargetFramework's property group!"));
            var version = GetTag(propertyGroup, VersionTag, InitialVersion);
            var assemblyVersion = GetTag(propertyGroup, AssemblyVersionTag, string.Empty);
            var fileVersion = GetTag(propertyGroup, FileVersionTag, string.Empty);

            var currentVersion = version.Value;
            var increasedVersion = IncreaseVersion(currentVersion);
            var newVersion = ResetBuild ? ResetDigit(increasedVersion) : increasedVersion;

            fileVersion.Value = assemblyVersion.Value = version.Value = newVersion;

            xdoc.Save(filepath);
            WriteObject(new {Project = filepath, NewVersion = newVersion, OldVersion = currentVersion});
        }

        string IncreaseVersion(string version){
            var result = Strategy switch{
                FullRevision => IncreaseVersion(Revision, IncreaseVersion(Build, version)),
                RevisionOnly => IncreaseVersion(Revision, version),
                NewMinor     => ResetVersion(IncreaseVersion(Minor, version), Revision),
                NewMajor     => ResetVersion(IncreaseVersion(Major, version), Minor, Revision),
                _            => version
            };
            return ResetBuild ? ResetVersion(result, Build) : result;
        }

        string ResetDigit(string version) {
            var parts = version.Split('.');
            var preverseDigits = Strategy switch{
                NewMajor => 1,
                NewMinor => 2,
                _        => 3
            };
            var newParts = parts.Take(preverseDigits).Concat(Enumerable.Repeat("0", 4 - preverseDigits));
            return string.Join('.', newParts);
        }

        const int Major = 0;
        const int Minor = 1;
        const int Revision = 2;
        const int Build = 3;
        static string IncreaseVersion(int digit, string version){
            var parts = version.Split('.');
            parts[digit] = (int.Parse(parts[digit]) + 1).ToString();
            return string.Join('.', parts);
        }

        static string ResetVersion(string version, params int[] digits){
            var parts = version.Split('.');
            digits.Iter(d => parts[d] = "0");
            return string.Join('.', parts);
        }

        static XElement GetTag(XElement xel, XName name, string initValue) =>
            xel.Descendants(name)
               .TryFirst()
               .Match(identity, () => new XElement(name, initValue).SideEffect(xel.Add));
    }
}
