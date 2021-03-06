using System;
using System.IO;

namespace CocoR
{
    public class Coco
    {
        public static int Main(string[] arg)
        {
            Console.WriteLine("Coco/R (March 29, 2016)");
            string srcName = null, nsName = null, frameDir = null, ddtString = null,
            traceFileName = null, outDir = null;
            bool emitLines = false;
            int retVal = 1;
            for (int i = 0; i < arg.Length; i++)
            {
                if (arg[i] == "-namespace" && i < arg.Length - 1) nsName = arg[++i].Trim();
                else if (arg[i] == "-frames" && i < arg.Length - 1) frameDir = arg[++i].Trim();
                else if (arg[i] == "-trace" && i < arg.Length - 1) ddtString = arg[++i].Trim();
                else if (arg[i] == "-o" && i < arg.Length - 1) outDir = arg[++i].Trim();
                else if (arg[i] == "-lines") emitLines = true;
                else srcName = arg[i];
            }
            if (arg.Length > 0 && srcName != null)
            {
                try
                {
                    string srcDir = Path.GetDirectoryName(srcName);

                    Scanner scanner = new Scanner(srcName);
                    Parser parser = new Parser(scanner);

                    traceFileName = Path.Combine(srcDir, "trace.txt");
                    parser.trace = new StreamWriter(new FileStream(traceFileName, FileMode.Create));
                    parser.tab = new Tab(parser);
                    parser.dfa = new DFA(parser);
                    parser.pgen = new ParserGen(parser);

                    parser.tab.srcName = srcName;
                    parser.tab.srcDir = srcDir;
                    parser.tab.nsName = nsName;
                    parser.tab.frameDir = frameDir;
                    parser.tab.outDir = (outDir != null) ? outDir : srcDir;
                    parser.tab.emitLines = emitLines;
                    if (ddtString != null) parser.tab.SetDDT(ddtString);

                    parser.Parse();

                    parser.trace.Close();
                    FileInfo f = new FileInfo(traceFileName);
                    if (f.Length == 0) f.Delete();
                    else Console.WriteLine("trace output is in " + traceFileName);
                    Console.WriteLine("{0} errors detected", parser.errors.count);
                    if (parser.errors.count == 0) { retVal = 0; }
                }
                catch (IOException)
                {
                    Console.WriteLine("-- could not open " + traceFileName);
                }
                catch (FatalError e)
                {
                    Console.WriteLine("-- " + e.Message);
                }
            }
            else {
                Console.WriteLine("Usage: Coco Grammar.ATG {{Option}}{0}"
                                  + "Options:{0}"
                                  + "  -namespace <namespaceName>{0}"
                                  + "  -frames    <frameFilesDirectory>{0}"
                                  + "  -trace     <traceString>{0}"
                                  + "  -o         <outputDirectory>{0}"
                                  + "  -lines{0}"
                                  + "Valid characters in the trace string:{0}"
                                  + "  A  trace automaton{0}"
                                  + "  F  list first/follow sets{0}"
                                  + "  G  print syntax graph{0}"
                                  + "  I  trace computation of first sets{0}"
                                  + "  J  list ANY and SYNC sets{0}"
                                  + "  P  print statistics{0}"
                                  + "  S  list symbol table{0}"
                                  + "  X  list cross reference table{0}"
                                  + "Scanner.frame and Parser.frame files needed in ATG directory{0}"
                                  + "or in a directory specified in the -frames option.",
                                  Environment.NewLine);
            }
            return retVal;
        }
    }
}