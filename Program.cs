using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace gethelmenvs
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // default to current path, OR we can specify a path
            string sDir = Directory.GetCurrentDirectory();
            
            if (args.Length > 0)
            {
                sDir = args[0];
            }

            // all that are close to a yaml file
            string []arrFiles = Directory.GetFiles(sDir, "*.y*", SearchOption.AllDirectories);
            string []arrHelmFiles = (from sFile in arrFiles where sFile.EndsWith(".yaml") || sFile.EndsWith(".yml") select sFile).ToArray();

            // process each file
            foreach (string sFile in arrHelmFiles)
            {
                FileStream fsFile = File.Open(sFile, FileMode.Open, FileAccess.Read, FileShare.Read);

                StreamReader srReader = new StreamReader(fsFile);
                string sYaml = await srReader.ReadToEndAsync();

                srReader.Close();
                fsFile.Close();
        
                List<EnvSetting> lstEnvs = await ScanFileForEnvs(sFile, sYaml);

                if (lstEnvs.Count > 0)
                {
                    Console.WriteLine("----K8s deployment file: " + sFile);
                    foreach (EnvSetting envSetting in lstEnvs)
                    {
                        Console.WriteLine(envSetting.Name + " - " + envSetting.Value);
                    }
                }
            }

        }

        // find env section that starts with tabs, may have some whitespace after it and definitely neds in a linefeed term
        static Regex _rxEnvSection = new Regex("(\\t*|\\s*)env:\\s*(\\r\\n|\\n)", RegexOptions.Compiled | RegexOptions.Multiline);

        static async Task<List<EnvSetting>> ScanFileForEnvs(string sFileName, string sFileText)
        {
            Match mcMatch = _rxEnvSection.Match(sFileText);

            List<EnvSetting> lstEnvSettings = new List<EnvSetting>();

            // keep scanning for env matches
            while (mcMatch != null && mcMatch.Success == true)
            {
                int iStartIndex = mcMatch.Index;

                string sMatchText = sFileText.Substring(mcMatch.Index + 1, mcMatch.Length - 1);
                
                // now we need to count the number of tabs or spaces
                int iTabOrSpaceCount = sMatchText.IndexOf("env");

                // now we need to scan ahead in the file text to find where the env section ends based upon the next section starting at same index that doesn't have a - for children

                // we read in rest of contents here because we are going to search for next section
                // why +4?  well the rest of yaml still has the whole env: section... so if we do this its enough to throw the next section match off so we can get the result we are looking for
                string sRestOfYaml = sFileText.Substring(mcMatch.Index + 1 + 4);

                // our search is from start of line always...
                string sDynamicRegex = "^" + (sMatchText[0] == '\t' ? "\\t" : "\\s");
                
                // finish off regex by putting specific count of tabs or spaces and making sure its a new section not a -
                sDynamicRegex += "{" + iTabOrSpaceCount + "}[A-Za-z]";

                Regex rxNextSection = new Regex(sDynamicRegex, RegexOptions.Multiline);
                Match mcNextSection = rxNextSection.Match(sRestOfYaml);

                string sEnvSection = "";

                // if we couldn't find a next section we are at the end
                if (mcNextSection == null || mcNextSection.Success == false)
                {
                    sEnvSection = sRestOfYaml;
                }
                else
                {
                    sEnvSection = sRestOfYaml.Substring(0, mcNextSection.Index);
                }

                ExtractEnvVarsFromEnvSection(sEnvSection, lstEnvSettings);

                // scan for a next match
                mcMatch = mcMatch.NextMatch();
            }

            // only keep unique ones
            return (from sEnv in lstEnvSettings select sEnv).Distinct().ToList();
        }

        // "(\\t*|\\s*)- name:\\s*([A-Za-z_]+)\\s*(\\r\\n|\\n)"
        static Regex _rxEnvName = new Regex("-\\s*name:\\s*([A-Za-z_]+)\\s*", RegexOptions.Compiled | RegexOptions.Multiline);

        // to extract the value OR we'll try to extract secret ref
        // we name our capture groups for easy reference because its an OR condition
        static Regex _rxEnvValue = new Regex("value:\\s*(?<EnvValue>\\S+)|valueFrom:[.\\n\\s]*secretKeyRef:[.\\n\\s]*name:\\s*\\S*[.\\n\\s]*key:\\s*(?<SecretMap>\\S+)");
/*
        - name: SFTP__FolderRoot
              valueFrom:
                secretKeyRef:
                  name: eih-secret
                  key: SFTP__FolderRoot_PeopleHub*/

        // sEnvSection here is an entire env: section of a container config
        static void ExtractEnvVarsFromEnvSection(string sEnvSection, List<EnvSetting> lstEnvSettings)
        {
            MatchCollection mcMatches = _rxEnvName.Matches(sEnvSection);

            // nothing to do if we have no matches
            if (mcMatches == null || mcMatches.Count == 0) { return; }

            // the match on envname is in the second group
            foreach (Match mcMatchEnvName in mcMatches)
            {
                // the first match and extract matches our env name.  
                EnvSetting envSetting = new EnvSetting();
                envSetting.Name = mcMatchEnvName.Groups[1].Value;

                // more work to be done... lets get the value of the env var OR lets get its mapping.
                // lets just get the piece of the value section that is right ahead of us
                string sValueExtract = sEnvSection.Substring(mcMatchEnvName.Index);

                // this will match a value OR a secret ref
                Match mcValue = _rxEnvValue.Match(sValueExtract);

                if (mcValue != null && mcValue.Success)
                {
                    // look for normal value by referencing the named capture group
                    string sValue = mcValue.Groups["EnvValue"].Value;

                    // if it wasn't there look for secret
                    if (sValue == "") { sValue = mcValue.Groups["SecretMap"].Value; }

                    envSetting.Value = sValue;
                }

                // make sure we add it to the list
                lstEnvSettings.Add(envSetting);
            }
        }

    }

    public class EnvSetting
    {
        public string Name { get; set; }

        public string Value { get; set; }
    }

    
}
