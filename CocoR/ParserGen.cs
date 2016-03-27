using System;
using System.Collections;
using System.IO;
using System.Text;

namespace CocoR
{
    public class ParserGen
    {
        const char CR = '\r';
        const char LF = '\n';
        const int EOF = -1;

        const int tErr = 0;			// error codes
        public const int altErr = 1;
        public const int syncErr = 2;

        public Position usingPos; // "using" definitions from the attributed grammar

        public int errorNr;      // highest parser error number
        FileStream fram = null;  // parser frame file
        StreamWriter gen; // generated parser source file
        StringWriter err = null; // generated parser error messages
        ArrayList symSet = new ArrayList();

        Tab tab;          // other Coco objects
        TextWriter trace;
        Errors errors;
        Buffer buffer;
        string insertionStart = "-->";
        string frameEnd = "$$$";

        void Indent(int n)
        {
            for (int i = 1; i <= n; i++) gen.Write('\t');
        }

        void CopyFramePart(string stop)
        {
            char startCh = stop[0];
            int endOfStopString = stop.Length - 1;
            int ch = fram.ReadByte();
            while (ch != EOF)
                if (ch == startCh)
                {
                    int i = 0;
                    do
                    {
                        if (i == endOfStopString) return; // stop[0..i] found
                        ch = fram.ReadByte(); i++;
                    } while (ch == stop[i]);
                    // stop[0..i-1] found; continue with last read character
                    gen.Write(stop.Substring(0, i));
                }
                else
                {
                    gen.Write((char)ch); ch = fram.ReadByte();
                }
            throw new FatalError(" -- incomplete or corrupt parser frame file");
        }

        /* ml
          returns the stop word - must start with insertionStart (-->)
          or frameEnd ($$$) - or null if none found
       */

        string CopyFramePart()
        {
            char startIn = insertionStart[0];
            int endOfInString = insertionStart.Length - 1;
            char startFr = frameEnd[0];
            int endOfFrString = frameEnd.Length - 1;
            StringBuilder insertion;
            int i;

            int ch = fram.ReadByte();
            while (ch != EOF)
            {
                if (ch == startIn)
                {
                    i = 0;
                    do
                    {
                        if (i == endOfInString)
                        { // insertion point found
                            insertion = new StringBuilder(insertionStart);
                            ch = fram.ReadByte();
                            while (!Char.IsWhiteSpace((char)ch))
                            {
                                insertion.Append((char)ch);
                                ch = fram.ReadByte();
                            }
                            return insertion.ToString();
                        }
                        ch = fram.ReadByte(); i++;
                    } while (ch == insertionStart[i]);
                    // insertionStart[0..i-1] found; continue with last read character
                    gen.Write(insertionStart.Substring(0, i));
                }
                else if (ch == startFr)
                {
                    i = 0;
                    do
                    {
                        if (i == endOfFrString)
                        { // end of frame found
                            return frameEnd;
                        }
                        ch = fram.ReadByte(); i++;
                    } while (ch == frameEnd[i]);
                    // frameEnd[0..i-1] found; continue with last read character
                    gen.Write(frameEnd.Substring(0, i));
                }
                else
                {
                    gen.Write((char)ch); ch = fram.ReadByte();
                }
            }
            return null;
        }

        public void CopySourcePart(Position pos, int indent)
        {
            // Copy text described by pos from atg to gen
            int ch, nChars, oldPos, i;
            if (pos != null)
            {
                oldPos = buffer.Pos;
                buffer.Pos = pos.beg; ch = buffer.Read(); nChars = pos.len - 1;
                Indent(indent);
                while (nChars >= 0)
                {
                    while (ch == CR || ch == LF)
                    {  // eol is either CR or CRLF or LF
                        gen.WriteLine(); Indent(indent);
                        if (ch == CR) { ch = buffer.Read(); nChars--; }  // skip CR
                        if (ch == LF) { ch = buffer.Read(); nChars--; }  // skip LF
                        for (i = 1; i <= pos.col && (ch == ' ' || ch == '\t'); i++)
                        {
                            // skip blanks at beginning of line
                            ch = buffer.Read(); nChars--;
                        }
                        if (i <= pos.col) pos.col = i - 1; // heading TABs => not enough blanks
                        if (nChars < 0) goto done;
                    }
                    gen.Write((char)ch);
                    ch = buffer.Read(); nChars--;
                }
                done:
                if (indent > 0) gen.WriteLine();
                buffer.Pos = oldPos;
            }
        }

        public void GenErrorMsg(int errTyp, Symbol sym)
        {
            errorNr++;
            err.Write("\t\t\t| " + errorNr + "-> s := \"");
            switch (errTyp)
            {
                case tErr:
                    if (sym.name[0] == '"') err.Write(tab.Escape(sym.name) + " expected");
                    else err.Write(sym.name + " expected");
                    break;

                case altErr: err.Write("invalid " + sym.name); break;
                case syncErr: err.Write("this symbol not expected in " + sym.name); break;
            }
            err.WriteLine("\"");
        }

        void OpenGen(string targetFile, bool backUp)
        { /* pdt */
            try
            {
                //ml string fn = tab.srcDir + "Scanner.cs"; /* pdt */
                if (File.Exists(targetFile) && backUp) File.Copy(targetFile, targetFile + ".old", true);
                gen = new StreamWriter(new FileStream(targetFile, FileMode.Create)); /* pdt */
            }
            catch (IOException)
            {
                throw new FatalError("incomplete or corrupt scanner frame file");
            }
        }

        public void WriteParser()
        {
            ParserOutput output = new ParserOutput(tab, errors, trace, buffer, usingPos);
            output.WriteParser();
        }

        public void WriteStatistics()
        {
            trace.WriteLine();
            trace.WriteLine("{0} terminals", tab.terminals.Count);
            trace.WriteLine("{0} symbols", tab.terminals.Count + tab.pragmas.Count +
                                           tab.nonterminals.Count);
            trace.WriteLine("{0} nodes", tab.nodes.Count);
            trace.WriteLine("{0} sets", symSet.Count);
        }

        public ParserGen(Parser parser)
        {
            tab = parser.tab;
            errors = parser.errors;
            trace = parser.trace;
            buffer = parser.scanner.buffer;
            errorNr = -1;
            usingPos = null;
        }
    } // end ParserGen
} // end namespace