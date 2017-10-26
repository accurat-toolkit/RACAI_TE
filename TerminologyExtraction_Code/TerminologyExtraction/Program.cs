using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using DataStructUtils;
using TerminologyExtraction.TTLService;
using Collocations;

namespace TerminologyExtraction
{
    class Program
    {
        //static Dictionary<string, string> htmlMap;
        static TTL ttlServ;

        static void Main(string[] args)
        {
            System.Net.ServicePointManager.Expect100Continue = false;
            ttlServ = new TTL();

            //htmlMap = readHtmlEnts("htmlEnt.txt");
            //Dictionary<string, double> ncounts = countNouns("TrainEn", "en");
            //DataStructWriter<string, double>.saveDictionary(ncounts, "enLanguageModel.txt", false, Encoding.UTF8, '\t', null, null);
            //Console.WriteLine("Done!");
            //Console.ReadLine();

            string lang = "ro";
            string languageModelFile = "roLM.txt";
            bool keepIntermedyaryFiles = false;
            bool alreadyProcessed = false;

            string inputFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--source")
                {
                    if (i + 1 < args.Length)
                    {
                        lang = args[i + 1].ToLower();
                        languageModelFile = lang + "LM.txt";
                    }
                }
                else if (args[i] == "--input")
                {
                    if (i + 1 < args.Length)
                    {
                        inputFile = args[i + 1];
                    }
                }
                else if (args[i] == "--param")
                {
                    if (i + 1 < args.Length && args[i + 1].ToLower() == "kif=true")
                    {
                        keepIntermedyaryFiles = true;
                    }
                    else if (i + 1 < args.Length && args[i + 1].ToLower() == "ap=true")
                    {
                        alreadyProcessed = true;
                    }
                }
            }

            if (inputFile == null)
            {
                Console.WriteLine("Usage: TerminologyExtraction.exe --input [FILE] [--source [LANG]] [--param [ap]=[TRUE]/[FALSE]] [--param [kif]=[TRUE]/[FALSE]]");
                //Console.WriteLine("Usage: TerminologyExtraction.exe --input [FILE] [--source [LANG]] [--param [kif]=[TRUE]/[FALSE]]");
                Console.WriteLine("Default Language: ro");
                Console.WriteLine("Default Already Preprocessed (RACAI's TTL preprocessing) <ap>: false");
                Console.WriteLine("Default Keep Intermediary Files <kif>: false");
            }
            else
            {
                if (!File.Exists("sgmlunic.ent"))
                {
                    Console.WriteLine("Error: file \"sgmlunic.ent\" is missing!");
                }
                else
                {
                    terminology(languageModelFile, inputFile, keepIntermedyaryFiles, lang, alreadyProcessed);
                }
            }
        }

        private static void terminology(string languageModelFile, string inputFile, bool keepIntermedyaryFiles, string lang, bool alreadyProcessed)
        {
            List<string> terms = new List<string>();

            //HashSet<string> generalTerms = new HashSet<string>();
            //if (lang == "ro")
            //{
            //    generalTerms = DataStructReader.readHashSet("gtRo.txt", Encoding.UTF8, 0, '\t', true, null);
            //}
            //else if (lang == "en")
            //{
            //    generalTerms = DataStructReader.readHashSet("gtEn.txt", Encoding.UTF8, 0, '\t', true, null);
            //}

            Dictionary<string, double> ncounts = DataStructReader.readDictionaryD(languageModelFile, Encoding.UTF8, 0, 1, '\t', false, null, null);
            if (ncounts.Count == 0)
            {
                Console.WriteLine("Language Model Missing... Press key for aborting!");
                Console.ReadLine();
            }
            else
            {
                Dictionary<string, double> userCounts = new Dictionary<string, double>();
                double total = 0;

                if (!File.Exists(inputFile))
                {
                    Console.WriteLine("Input File doesn't exist... Press key for aborting!");
                    Console.ReadLine();
                }
                else
                {
                    Dictionary<string, string> fileCorrespondences = new Dictionary<string, string>();
                    string line = "";
                    StreamReader rdr = new StreamReader(inputFile, Encoding.UTF8);
                    while ((line = rdr.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split('\t');
                        if (!fileCorrespondences.ContainsKey(parts[0]))
                        {
                            fileCorrespondences.Add(parts[0], parts[1]);
                        }
                    }

                    string[] files = fileCorrespondences.Keys.ToArray();
                    Dictionary<string, Dictionary<string, int>> singleOccurencesFirst = new Dictionary<string, Dictionary<string, int>>();
                    StreamWriter wrtProcessed = new StreamWriter("_preprocessed", false, Encoding.UTF8);
                    wrtProcessed.AutoFlush = true;

                    foreach (string file in files)
                    {
                        if (alreadyProcessed)
                        {
                            Console.Write("\nReading file: {0}", file);
                        }
                        else
                        {
                            Console.WriteLine("\nProcessing file: {0}", file);
                        }
                        getUserCounts(ref userCounts, ref singleOccurencesFirst, file, wrtProcessed, ref total, lang, alreadyProcessed);
                        //Console.WriteLine(" ... done!");
                    }
                    wrtProcessed.Close();

                    Console.Write("Extracting single word terms");

                    foreach (string key in userCounts.Keys.ToArray())
                    {
                        if (userCounts[key] < 2 /*|| generalTerms.Contains(key)*/)
                        {
                            userCounts.Remove(key);
                        }
                        else
                        {
                            userCounts[key] = userCounts[key] / total;
                        }
                    }

                    Dictionary<string, List<string>> singleOccurences = getSingle(singleOccurencesFirst);

                    Dictionary<string, double> results = new Dictionary<string, double>();
                    foreach (string word in userCounts.Keys)
                    {
                        double newVal = 0;

                        if (ncounts.ContainsKey(word))
                        {
                            newVal = userCounts[word] / ncounts[word];
                        }
                        else
                        {
                            newVal = userCounts[word] / ncounts["_dummy_"];
                        }

                        results.Add(word, newVal);
                    }

                    string[] keys = results.Keys.ToArray();
                    double[] values = results.Values.ToArray();

                    Array.Sort(values, keys);

                    StreamWriter wrt = new StreamWriter("_monoTerms", false, Encoding.UTF8);
                    wrt.AutoFlush = true;
                    for (int i = keys.Length - 1; i >= 0; i--)
                    {
                        wrt.WriteLine("{0}\t{1}", keys[i], values[i]);
                    }
                    wrt.Close();

                    Console.WriteLine(" ... done!");

                    Console.Write("Extracting multi word terms");

                    ColocationExtractor ce = new ColocationExtractor();
                    Dictionary<string, List<string>> multiOccurences = new Dictionary<string, List<string>>();

                    if (ce.extractCollocations("_preprocessed", "_multiTerms"))
                    {
                        Console.WriteLine(" ... done!");

                        Console.Write("Create index for extracting exact occurences");

                        if (!Directory.Exists("_index"))
                        {
                            Directory.CreateDirectory("_index");
                        }
                        ce.indexText("_preprocessed", "_index");
                        Console.WriteLine(" ... done!");

                        Console.Write("Search for exact occurences");
                        Lucene.Net.Search.IndexSearcher searcher = new Lucene.Net.Search.IndexSearcher("_index");
                        multiOccurences = retriveOccurences(ce, searcher, "_multiTerms");
                        Console.WriteLine(" ... done!");

                        searcher.Close();
                        string[] filesToDel = Directory.GetFiles("_index");
                        foreach (string f in filesToDel)
                        {
                            File.Delete(f);
                        }
                        Directory.Delete("_index");
                    }
                    else
                    {
                        Console.WriteLine(" ... done! - no multi word terms found!");
                    }


                    Console.Write("Retrieving terminology");

                    terms = extractTerminology("_monoTerms", "_multiTerms", singleOccurences, multiOccurences);

                    if (keepIntermedyaryFiles)
                    {
                        StreamWriter wrtT = new StreamWriter("_terminology", false, Encoding.UTF8);
                        wrtT.AutoFlush = true;
                        foreach (string term in terms)
                        {
                            wrtT.WriteLine(term);
                        }
                        wrtT.Close();
                    }

                    Console.WriteLine(" ... done!");

                    HashSet<string> mono = new HashSet<string>();
                    Dictionary<string, HashSet<string>> multi = new Dictionary<string, HashSet<string>>();
                    HashSet<string> multiOrg = new HashSet<string>();

                    getTerms(terms, ref mono, ref multi, ref multiOrg);
                    markTerms(lang, fileCorrespondences, mono, multi, multiOrg, alreadyProcessed);

                    if (!keepIntermedyaryFiles)
                    {
                        File.Delete("_preprocessed");
                        File.Delete("_monoTerms");
                        File.Delete("_multiTerms");
                    }
                }
            }
        }

        private static void markTerms(string lang, Dictionary<string, string> fileCorrespondences, HashSet<string> mono, Dictionary<string, HashSet<string>> multi, HashSet<string> multiOrg, bool alreadyProcessed)
        {
            foreach (string file in fileCorrespondences.Keys)
            {
                Console.Write("Annotating {0}... ", Path.GetFileName(file));

                StreamWriter wrt = new StreamWriter(fileCorrespondences[file], false, Encoding.UTF8);
                wrt.AutoFlush = true;

                if (!alreadyProcessed)
                {
                    //textul NU e deja preprocesat
                    StreamReader rdr = new StreamReader(file, Encoding.UTF8);
                    string line = "";

                    Regex regex = new Regex("(?<word>[\\w-]+)|(?<char>.)", RegexOptions.None);
                    StringBuilder sb = new StringBuilder();
                    bool inside = false;

                    while ((line = rdr.ReadLine()) != null)
                    {
                        Match m = regex.Match(line);
                        while (m.Success)
                        {
                            string val = m.Groups["word"].Value;
                            if (val != "" && (mono.Contains(val.ToLower()) || multi.ContainsKey(val.ToLower()) || inside))
                            {
                                if (!inside)
                                {
                                    if (!multi.ContainsKey(val.ToLower()))
                                    {
                                        wrt.Write("<TENAME>" + val + "</TENAME>");
                                    }
                                    else
                                    {
                                        sb.Append("<TENAME>" + val);
                                        inside = true;
                                    }
                                }
                                else
                                {
                                    string key = sb.ToString().Substring(9).ToLower().Trim();
                                    if (multi.ContainsKey(key) && multi[key].Contains(val.ToLower()))
                                    {
                                        sb.Append(val);
                                    }
                                    else
                                    {
                                        wrt.Write(sb.ToString().Trim() + "</TENAME>");
                                        wrt.Write(" " + val);
                                        inside = false;
                                        sb = new StringBuilder();
                                    }
                                }
                            }
                            else
                            {
                                if (!inside)
                                {
                                    wrt.Write(m.Value);
                                }
                                else
                                {
                                    if (m.Value != " ")
                                    {
                                        wrt.Write(sb.ToString() + "</TENAME>");
                                        wrt.Write(m.Value);
                                        inside = false;
                                        sb = new StringBuilder();
                                    }
                                    else
                                    {
                                        sb.Append(" ");
                                    }
                                }
                            }

                            m = m.NextMatch();
                        }
                        wrt.WriteLine();
                    }

                    rdr.Close();
                }
                else
                {
                    //textul e deja preprocesat
                    string text = DataStructReader.readWholeTextFile(file, Encoding.UTF8);
                    Regex regex = new Regex(
                          "<seg lang=\"" + lang + "\">.+?</seg>",
                        RegexOptions.Singleline
                        );

                    Match m = regex.Match(text);

                    while (m.Success)
                    {
                        try
                        {
                            XmlDocument xdoc = new XmlDocument();
                            xdoc.LoadXml("<!DOCTYPE root [<!ENTITY % SGMLUniq SYSTEM \"sgmlunic.ent\"> %SGMLUniq;]>\n<root>" + m.Value.Replace("", "").Replace("\x01", "").Replace("\x1B", "").Replace("&b.theta;", "&b.Theta;") + "</root>");
                            XmlNodeList list = xdoc.SelectNodes("//w|//c");

                            StringBuilder sb = new StringBuilder();
                            bool inside = false;
                            StringBuilder sentence = new StringBuilder();
                            string firstPos = "";

                            foreach (XmlNode node in list)
                            {
                                bool alreadyAdded = false;

                                if (node.Name == "w")
                                {
                                    string val = node.InnerText.Replace("_", " ");
                                    string pos = node.Attributes["ana"].InnerText.Substring(0, 1).ToLower();

                                    if (val != "" && (mono.Contains(val.ToLower()) || multi.ContainsKey(val.ToLower()) || inside))
                                    {
                                        if (!inside)
                                        {
                                            if (pos == "n" || pos == "a")
                                            {
                                                if (!multi.ContainsKey(val.ToLower()))
                                                {
                                                    if (pos == "n")
                                                    {
                                                        sentence.Append(" <TENAME>" + val + "</TENAME>");
                                                    }
                                                }
                                                else
                                                {
                                                    sb.Append(" <TENAME>" + val);
                                                    firstPos = pos;
                                                    inside = true;
                                                }
                                            }
                                            else
                                            {
                                                sentence.Append(" " + val);
                                            }
                                        }
                                        else
                                        {
                                            string key = sb.ToString().Trim().Substring(8).ToLower().Trim();
                                            if (multi.ContainsKey(key) && multi[key].Contains(val.ToLower()))
                                            {
                                                sb.Append(" " + val);
                                            }
                                            else
                                            {
                                                string toAdd = sb.ToString().Trim().Substring(8).Trim();
                                                if (multiOrg.Contains(toAdd.ToLower()))
                                                {
                                                    sentence.Append(" <TENAME>" + toAdd + "</TENAME>");
                                                }
                                                else
                                                {
                                                    int idx = toAdd.IndexOf(' ');

                                                    if (idx != -1)
                                                    {
                                                        string first = toAdd.Substring(0, idx);
                                                        string rest = toAdd.Substring(idx + 1);

                                                        if (mono.Contains(first.ToLower()) && firstPos == "n")
                                                        {
                                                            sentence.Append(" <TENAME>" + first + "</TENAME> " + rest);
                                                        }
                                                        else
                                                        {
                                                            sentence.Append(" " + toAdd);
                                                        }
                                                    }
                                                    else if (firstPos == "n")
                                                    {
                                                        sentence.Append(" <TENAME>" + toAdd + "</TENAME>");
                                                    }
                                                    else
                                                    {
                                                        sentence.Append(" " + toAdd);
                                                    }
                                                }

                                                sentence.Append(" " + val);
                                                inside = false;
                                                sb = new StringBuilder();
                                            }
                                        }

                                        alreadyAdded = true;
                                    }
                                }

                                if (!inside)
                                {
                                    if (!alreadyAdded)
                                    {
                                        sentence.Append(" " + node.InnerText.Replace("_", " "));
                                    }
                                }
                                else
                                {
                                    if (!alreadyAdded)
                                    {
                                        if (m.Value != " ")
                                        {
                                            sentence.Append(" " + sb.ToString().Trim() + "</TENAME>");
                                            sentence.Append(" " + node.InnerText.Replace("_", " "));
                                            inside = false;
                                            sb = new StringBuilder();
                                        }
                                        else
                                        {
                                            sb.Append(" ");
                                        }
                                    }
                                }
                            }
                            wrt.WriteLine(sentence.ToString().Trim());
                        }
                        catch
                        {
                        }
                        m = m.NextMatch();
                    }
                }
                wrt.Close();

                Console.WriteLine("done.");
            }
        }

        private static void getTerms(List<string> terms, ref HashSet<string> mono, ref Dictionary<string, HashSet<string>> multi, ref HashSet<string> multiOrg)
        {
            foreach (string fullterm in terms)
            {
                string term = fullterm.Split('\t')[0];
                if (!term.Contains(" "))
                {
                    mono.Add(term);
                }
                else
                {
                    multiOrg.Add(term);
                    string[] tokens = term.Split(' ');

                    for (int i = 0; i < tokens.Length - 1; i++)
                    {
                        string first = string.Join(" ", tokens, 0, i + 1);
                        string second = tokens[i + 1];

                        if (!multi.ContainsKey(first))
                        {
                            multi.Add(first, new HashSet<string>());
                        }
                        multi[first].Add(second);
                    }
                }
            }
        }

        private static Dictionary<string, List<string>> getSingle(Dictionary<string, Dictionary<string, int>> singleOccurencesFirst)
        {
            Dictionary<string, List<string>> ret = new Dictionary<string, List<string>>();
            foreach (string key in singleOccurencesFirst.Keys)
            {
                if (singleOccurencesFirst[key].Count > 0)
                {
                    ret.Add(key, new List<string>());

                    string[] ks = singleOccurencesFirst[key].Keys.ToArray();
                    int[] vs = singleOccurencesFirst[key].Values.ToArray();

                    Array.Sort(vs, ks);

                    for (int i = ks.Length - 1; i >= 0; i--)
                    {
                        ret[key].Add(ks[i] + "\t" + vs[i]);
                    }
                }

            }
            return ret;
        }

        private static Dictionary<string, List<string>> retriveOccurences(ColocationExtractor ce, Lucene.Net.Search.IndexSearcher searcher, string fileName)
        {
            Dictionary<string, List<string>> ret = new Dictionary<string, List<string>>();
            StreamReader rdr = new StreamReader(fileName, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                line = line.Trim();
                string[] tokens = line.Split(' ');
                List<string> str = ce.retrieveExpression(searcher, line);
                ret.Add(tokens[0] + " " + tokens[1], str);
            }
            rdr.Close();
            return ret;
        }

        private static List<string> extractTerminology(string monoTermsFile, string multiTermsFile, Dictionary<string, List<string>> singleOccurences, Dictionary<string, List<string>> multiOccurences)
        {
            List<string> ret = new List<string>();

            Dictionary<string, HashSet<string>> terms = new Dictionary<string, HashSet<string>>();
            Dictionary<string, double> mono = getInfo(monoTermsFile, true, ref terms);
            Dictionary<string, HashSet<string>> mterms = new Dictionary<string, HashSet<string>>();
            Dictionary<string, double> multi = getInfo(multiTermsFile, false, ref mterms);

            Dictionary<string, double> results = new Dictionary<string, double>();
            foreach (string key in mono.Keys)
            {
                double total = mono[key];
                if (multi.Keys.Contains(key))
                {
                    total = (total + multi[key]) / 2.0;
                }

                results.Add(key, total);
            }

            string[] ks = results.Keys.ToArray();
            double[] vs = results.Values.ToArray();
            Array.Sort(vs, ks);

            for (int i = 0; i < ks.Length; i++)
            {
                bool ok = true;
                if (singleOccurences.ContainsKey(ks[i]))
                {
                    string score = terms[ks[i]].ToArray()[0];
                    score = score.Substring(score.IndexOf("\t") + 1);
                    double sc = double.Parse(score);

                    if (sc < 250)
                    {
                        ok = false;
                    }

                    //if (ok && singleOccurences[ks[i]].Count == 1)
                    //{
                    //    if (singleOccurences[ks[i]][0].Split('\t')[1] == "1")
                    //    {
                    //        ok = false;
                    //    }
                    //}

                    if (ok)
                    {
                        for (int j = 0; j < singleOccurences[ks[i]].Count; j++)
                        {
                            ret.Add(singleOccurences[ks[i]][j].Replace("_", " ") + "\t" + score);
                            //wrt.WriteLine("{0}\t{1}", singleOccurences[ks[i]][j].Replace("_", " "), score);
                        }
                    }
                }
                if (ok && mterms.ContainsKey(ks[i]))
                {
                    foreach (string val in mterms[ks[i]])
                    {
                        string[] parts = val.Split('\t');
                        if (multiOccurences.ContainsKey(parts[0]))
                        {
                            for (int j = 0; j < multiOccurences[parts[0]].Count; j++)
                            {
                                ret.Add(multiOccurences[parts[0]][j].Replace("_", " ") + "\t" + parts[1]);
                                //wrt.WriteLine("{0}\t{1}", multiOccurences[parts[0]][j].Replace("_", " "), parts[1]);
                            }
                        }
                    }
                }
            }

            return ret;
        }

        private static Dictionary<string, double> getInfo(string termsFile, bool monoTerms, ref Dictionary<string, HashSet<string>> terms)
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();

            StreamReader rdr = new StreamReader(termsFile, Encoding.UTF8);
            string line = "";
            double position = 0;
            while ((line = rdr.ReadLine()) != null)
            {
                position++;
                if (monoTerms)
                {
                    string word = line.Trim().Split('\t')[0];
                    if (!ret.ContainsKey(word))
                    {
                        ret.Add(word, position);
                        terms.Add(word, new HashSet<string>() { line });
                    }
                }
                else
                {
                    line = line.Trim();
                    string[] tokens = line.Split(' ');
                    string[] wordsList = new string[2] { tokens[0], tokens[1] };

                    foreach (string word in wordsList)
                    {
                        if (!terms.ContainsKey(word))
                        {
                            terms.Add(word, new HashSet<string>());
                        }
                        terms[word].Add(tokens[0] + " " + tokens[1] + "\t" + tokens[6]);

                        if (!ret.ContainsKey(word))
                        {
                            ret.Add(word, position);
                        }
                    }
                }
            }
            rdr.Close();

            return ret;
        }

        private static void getUserCounts(ref Dictionary<string, double> counts, ref Dictionary<string, Dictionary<string, int>> singleOccurences, string fileName, StreamWriter wrt, ref double total, string lang, bool alreadyProcessed)
        {
            string text = DataStructReader.readWholeTextFile(fileName, Encoding.UTF8);

            if (!alreadyProcessed)
            {
                string[] parts = text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    string[] xmlPieces = null;

                    xmlPieces = preprocess(parts[i], lang).Trim().Split('\n');

                    foreach (string xmlText in xmlPieces)
                    {
                        process(xmlText, ref counts, ref singleOccurences, ref total, wrt);
                    }
                    Console.Write("\r{0}%   ", 100 * (i + 1) / parts.Length);
                }
            }
            else
            {
                Regex regex = new Regex(
                      "<seg lang=\"" + lang + "\">.+?</seg>",
                    RegexOptions.Singleline
                    );
                Match m = regex.Match(text);
                while (m.Success)
                {
                    process(m.Value, ref counts, ref singleOccurences, ref total, wrt);
                    m = m.NextMatch();
                }
            }
        }

        static string preprocess(string text, string lang)
        {
            try
            {
                text = ttlServ.UTF8toSGML(text);
                text = ttlServ.XCES(lang, lang, text);
                text = ttlServ.SGMLtoUTF7(text);
                ASCIIEncoding asciiEnc = new ASCIIEncoding();
                text = Encoding.UTF8.GetString(UTF7Encoding.Convert(Encoding.UTF7, Encoding.UTF8, asciiEnc.GetBytes(text)));
                return text;
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        private static Dictionary<string, string> readHtmlEnts(string fileName)
        {
            Dictionary<string, string> hash = new Dictionary<string, string>();

            StreamReader rdr = new StreamReader(fileName, Encoding.UTF8);
            string line = "";
            while ((line = rdr.ReadLine()) != null)
            {
                string[] parts = line.Split('\t');

                if (parts.Length == 3)
                {
                    if (parts[1] != "" && !hash.ContainsKey(parts[1]))
                    {
                        hash.Add(parts[1], parts[0]);
                    }
                }
                if (parts.Length == 4)
                {
                    if (parts[1] != "" && !hash.ContainsKey(parts[1]))
                    {
                        hash.Add(parts[1], parts[0]);
                    }
                    if (parts[2] != "" && !hash.ContainsKey(parts[2]))
                    {
                        hash.Add(parts[2], parts[0]);
                    }
                }

            }
            rdr.Close();

            return hash;
        }

        private static Dictionary<string, double> countNouns(string folder, string lang, HashSet<string> generalTerms)
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();

            Dictionary<string, double> counts = new Dictionary<string, double>();
            double total = 0;

            Dictionary<string, Dictionary<string, int>> singleOccurences = new Dictionary<string, Dictionary<string, int>>();

            string[] files = Directory.GetFiles(folder);

            foreach (string fileName in files)
            {
                StreamReader rdr = new StreamReader(fileName, Encoding.UTF8);
                string line = "";
                while ((line = rdr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("<seg lang=\"" + lang + "\">"))
                    {
                        process(line, ref counts, ref singleOccurences, ref total, null);
                    }
                }
                rdr.Close();
            }

            double vocabulary = counts.Keys.Count;

            Dictionary<double, double> noWords = new Dictionary<double, double>();
            foreach (string key in counts.Keys)
            {
                if (!noWords.ContainsKey(counts[key]))
                {
                    noWords.Add(counts[key], 1);
                }
                else
                {
                    noWords[counts[key]]++;
                }
            }


            foreach (string key in counts.Keys.ToArray())
            {
                double howMany = 1.0;
                if (noWords.ContainsKey(counts[key] + 1))
                {
                    howMany = noWords[counts[key] + 1.0];
                }
                ret[key] = ((counts[key] + 1) / (total + vocabulary)) * howMany / noWords[counts[key]];
            }

            double e1 = noWords[1] / total;
            ret.Add("_dummy_", e1 / ((total + vocabulary) * 1000));

            return ret;
        }

        private static void process(string line, ref Dictionary<string, double> ret, ref Dictionary<string, Dictionary<string, int>> singleOccurences, ref double total, StreamWriter wrt)
        {
            string xmlText = line;
            //if (htmlMap != null)
            //{
            //    xmlText = Regex.Replace(line, "&.*?;", new MatchEvaluator(modMatched));
            //}

            try
            {
                XmlDocument xdoc = new XmlDocument();
                xdoc.LoadXml("<!DOCTYPE root [<!ENTITY % SGMLUniq SYSTEM \"sgmlunic.ent\"> %SGMLUniq;]>\n<root>" + xmlText + "</root>");
                XmlNodeList list = xdoc.SelectNodes("//w|//c");

                foreach (XmlNode node in list)
                {
                    string pos = "PUNCT";
                    string occurence = node.InnerText;
                    string lemma = occurence;

                    if (node.Attributes["lemma"] != null)
                    {
                        lemma = node.Attributes["lemma"].InnerText.ToLower();
                        if (lemma.Contains(")"))
                        {
                            lemma = lemma.Substring(lemma.IndexOf(")") + 1);
                        }
                        pos = node.Attributes["ana"].InnerText;
                    }

                    if (wrt != null)
                    {
                        wrt.WriteLine("{0}\t{1}\t{2}", occurence, pos, lemma);
                    }

                    if (pos.ToLower().StartsWith("n"))
                    {
                        if (ret.ContainsKey(lemma))
                        {
                            ret[lemma]++;
                        }
                        else
                        {
                            ret.Add(lemma, 1);
                        }

                        occurence = occurence.ToLower();
                        if (!singleOccurences.ContainsKey(lemma))
                        {
                            singleOccurences.Add(lemma, new Dictionary<string, int>());
                        }

                        if (!singleOccurences[lemma].ContainsKey(occurence))
                        {
                            singleOccurences[lemma].Add(occurence, 1);
                        }
                        else
                        {
                            singleOccurences[lemma][occurence]++;
                        }

                        total++;
                    }
                }
            }
            catch
            {
                //Console.WriteLine("Error: {0}", line);
            }
        }

        //static string modMatched(Match m)
        //{
        //    string x = m.ToString();
        //    if (htmlMap.ContainsKey(x))
        //    {
        //        return htmlMap[x];
        //    }
        //    return x;
        //}
    }
}
