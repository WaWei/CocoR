using System;
using System.Collections;
using System.IO;
using System.Text;

namespace CocoR
{
    public class ParserOutput
    {
        const int maxTerm = 3;		// sets of size < maxTerm are enumerated
        const char CR = '\r';
        const char LF = '\n';
        const int EOF = -1;

        const int tErr = 0;			// error codes
        const int altErr = 1;
        const int syncErr = 2;

        public Position usingPos; // "using" definitions from the attributed grammar
        int errorNr;      // highest parser error number
        Symbol curSy;     // symbol whose production is currently generated
        FileStream fram;  // parser frame file
        StringBuilder gen = new StringBuilder(); // generated parser source file
        StringWriter err; // generated parser error messages
        StreamWriter gen2;
        ArrayList symSet = new ArrayList();

        Tab tab;          // other Coco objects
        TextWriter trace;
        Errors errors;
        Buffer buffer;

        void Indent(int n)
        {
            for (int i = 1; i <= n; i++) gen.Append('\t');
        }

        bool Overlaps(BitArray s1, BitArray s2)
        {
            int len = s1.Count;
            for (int i = 0; i < len; ++i)
            {
                if (s1[i] && s2[i])
                {
                    return true;
                }
            }
            return false;
        }

        // use a switch if more than 5 alternatives and none starts with a resolver
        bool UseSwitch(Node p)
        {
            BitArray s1, s2;
            if (p.typ != Node.alt) return false;
            int nAlts = 0;
            s1 = new BitArray(tab.terminals.Count);
            while (p != null)
            {
                s2 = tab.Expected0(p.sub, curSy);
                // must not optimize with switch statement, if there are ll1 warnings
                if (Overlaps(s1, s2)) { return false; }
                s1.Or(s2);
                ++nAlts;
                // must not optimize with switch-statement, if alt uses a resolver expression
                if (p.sub.typ == Node.rslv) return false;
                p = p.down;
            }
            return nAlts > 5;
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
                    gen.Append(stop.Substring(0, i));
                }
                else
                {
                    gen.Append((char)ch);
                    ch = fram.ReadByte();
                }
            throw new FatalError("Incomplete or corrupt parser frame file");
        }

        void CopySourcePart(Position pos, int indent)
        {
            // Copy text described by pos from atg to gen
            int ch, nChars, i;
            if (pos != null)
            {
                buffer.Pos = pos.beg; ch = buffer.Read(); nChars = pos.len - 1;
                Indent(indent);
                while (nChars >= 0)
                {
                    while (ch == CR || ch == LF)
                    {  // eol is either CR or CRLF or LF
                        gen.AppendLine(); Indent(indent);
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
                    gen.Append((char)ch);
                    ch = buffer.Read(); nChars--;
                }
                done:
                if (indent > 0) gen.AppendLine();
            }
        }

        void GenErrorMsg(int errTyp, Symbol sym)
        {
            errorNr++;
            err.Write("\t\t\t|" + errorNr + " ->  \"");
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

        int NewCondSet(BitArray s)
        {
            for (int i = 1; i < symSet.Count; i++) // skip symSet[0] (reserved for union of SYNC sets)
                if (Sets.Equals(s, (BitArray)symSet[i])) return i;
            symSet.Add(s.Clone());
            return symSet.Count - 1;
        }

        void GenCond(BitArray s, Node p)
        {
            if (p.typ == Node.rslv) CopySourcePart(p.pos, 0);
            else
            {
                int n = Sets.Elements(s);
                if (n == 0) gen.Append("false"); // should never happen
                else if (n <= maxTerm)
                    foreach (Symbol sym in tab.terminals)
                    {
                        if (s[sym.n])
                        {
                            gen.Append("x.la.kind = " + sym.n);
                            --n;
                            if (n > 0) gen.Append(" || ");
                        }
                    }
                else
                    gen.Append("x.StartOf(" + NewCondSet(s) + ")");
                /*
                if (p.typ == Node.alt) {
                    // for { ... | IF ... | ... } or [ ... | IF ... | ... ]
                    // check resolvers in addition to terminal start symbols of alternatives
                    Node q = p;
                    while (q != null) {
                        if (q.sub.typ == Node.rslv) {
                            gen.Append(" || ");
                            CopySourcePart(q.sub.pos, 0);
                        }
                        q = q.down;
                    }
                }
                */
            }
        }

        void PutCaseLabels(BitArray s)
        {
            int i = 0;
            foreach (Symbol sym in tab.terminals)
                if (s[sym.n])
                {
                    gen.Append("|" + sym.n + " "); i++;
                }
            if (i != 0) gen.Append("-> ");
        }

        void GenCode(Node p, int indent, BitArray isChecked)
        {
            Node p2;
            BitArray s1, s2;
            while (p != null)
            {
                switch (p.typ)
                {
                    case Node.nt:
                        {
                            Indent(indent);
                            gen.Append("x." + p.sym.name + "(");
                            CopySourcePart(p.pos, 0);
                            gen.AppendLine(");");
                            break;
                        }
                    case Node.t:
                        {
                            Indent(indent);
                            if (isChecked[p.sym.n]) gen.AppendLine("x.Get();");
                            else gen.AppendLine("x.Expect(" + p.sym.n + ");");
                            break;
                        }
                    case Node.wt:
                        {
                            Indent(indent);
                            s1 = tab.Expected(p.next, curSy);
                            s1.Or(tab.allSyncSets);
                            gen.AppendLine("x.ExpectWeak(" + p.sym.n + ", " + NewCondSet(s1) + ");");
                            break;
                        }
                    case Node.any:
                        {
                            Indent(indent);
                            gen.AppendLine("x.Get();");
                            break;
                        }
                    case Node.eps: break; // nothing
                    case Node.rslv: break; // nothing
                    case Node.sem:
                        {
                            CopySourcePart(p.pos, indent);
                            break;
                        }
                    case Node.sync:
                        {
                            Indent(indent);
                            GenErrorMsg(syncErr, curSy);
                            s1 = (BitArray)p.set.Clone();
                            gen.Append("while (not("); GenCond(s1, p); gen.Append(")) do ");
                            gen.Append("x.SynErr(+" + errorNr + "); x.Get();"); gen.AppendLine("done;");
                            break;
                        }
                    case Node.alt:
                        {
                            s1 = tab.First(p);
                            bool equal = Sets.Equals(s1, isChecked);
                            bool useSwitch = UseSwitch(p);
                            if (useSwitch) { Indent(indent); gen.AppendLine("match x.la.kind with"); }
                            p2 = p;
                            while (p2 != null)
                            {
                                s1 = tab.Expected(p2.sub, curSy);
                                Indent(indent);
                                if (useSwitch)
                                {
                                    PutCaseLabels(s1); gen.AppendLine("( ");
                                }
                                else if (p2 == p)
                                {
                                    gen.Append("if ("); GenCond(s1, p2.sub); gen.AppendLine(") then (");
                                }
                                else if (p2.down == null && equal)
                                {
                                    gen.AppendLine(") else (");
                                }
                                else
                                {
                                    gen.Append(") else if ("); GenCond(s1, p2.sub); gen.AppendLine(") then ( ");
                                }
                                s1.Or(isChecked);
                                GenCode(p2.sub, indent + 1, s1);
                                if (useSwitch)
                                {
                                    Indent(indent);
                                    Indent(indent); gen.AppendLine(");");
                                }
                                p2 = p2.down;
                            }
                            Indent(indent);
                            if (equal)
                            {
                                gen.AppendLine(");");
                            }
                            else
                            {
                                GenErrorMsg(altErr, curSy);
                                if (useSwitch)
                                {
                                    gen.AppendLine("|_-> x.SynErr(" + errorNr + ");");
                                    Indent(indent);
                                }
                                else
                                {
                                    gen.Append(") "); gen.AppendLine("else x.SynErr(" + errorNr + ");");
                                }
                            }
                            break;
                        }
                    case Node.iter:
                        {
                            Indent(indent);
                            p2 = p.sub;
                            gen.Append("while (");
                            if (p2.typ == Node.wt)
                            {
                                s1 = tab.Expected(p2.next, curSy);
                                s2 = tab.Expected(p.next, curSy);
                                gen.Append("x.WeakSeparator(" + p2.sym.n + ", " + NewCondSet(s1) + ", " + NewCondSet(s2) + ") ");
                                s1 = new BitArray(tab.terminals.Count);  // for inner structure
                                if (p2.up || p2.next == null) p2 = null; else p2 = p2.next;
                            }
                            else
                            {
                                s1 = tab.First(p2);
                                GenCond(s1, p2);
                            }
                            gen.AppendLine(") do ");
                            GenCode(p2, indent + 1, s1);
                            Indent(indent); gen.AppendLine(" done;");
                            break;
                        }
                    case Node.opt:
                        s1 = tab.First(p.sub);
                        Indent(indent);
                        gen.Append("if ("); GenCond(s1, p.sub); gen.AppendLine(") then (");
                        GenCode(p.sub, indent + 1, s1);
                        Indent(indent); gen.AppendLine(");");
                        break;
                }
                if (p.typ != Node.eps && p.typ != Node.sem && p.typ != Node.sync)
                    isChecked.SetAll(false);
                if (p.up) break;
                p = p.next;
            }
        }

        void GenTokens()
        {
            foreach (Symbol sym in tab.terminals)
            {
                if (Char.IsLetter(sym.name[0]))
                    gen.AppendLine("\tval _" + sym.name + " : int;");
            }
            gen.AppendLine("\tval maxT :int;");
        }

        void GenTokens2()
        {
            foreach (Symbol sym in tab.terminals)
            {
                if (Char.IsLetter(sym.name[0]))
                    gen.AppendLine("\t\t_" + sym.name + " = " + sym.n + ";");
            }
            gen.AppendLine("\t\tmaxT = " + (tab.terminals.Count - 1) + ";");
        }

        void GenPragmas()
        {
            foreach (Symbol sym in tab.pragmas)
            {
                gen.AppendLine("\tval _" + sym.name + " : int");
            }
        }

        void GenPragmas2()
        {
            foreach (Symbol sym in tab.pragmas)
            {
                gen.AppendLine("\t _" + sym.name + " = " + sym.n + ";");
            }
        }

        void GenCodePragmas()
        {
            foreach (Symbol sym in tab.pragmas)
            {
                gen.AppendLine("\t\t\t\tif (x.la.kind = " + sym.n + ") then (");
                CopySourcePart(sym.semPos, 4);
                gen.AppendLine("\t\t\t\t);");
            }
        }

        void GenProductions()
        {
            foreach (Symbol sym in tab.nonterminals)
            {
                curSy = sym;
                gen.Append("\tmember x." + sym.name + " (");
                CopySourcePart(sym.attrPos, 0);
                gen.AppendLine(") =");
                CopySourcePart(sym.semPos, 2);
                GenCode(sym.graph, 2, new BitArray(tab.terminals.Count));
                gen.AppendLine();
            }
        }

        void InitSets()
        {
            for (int i = 0; i < symSet.Count; i++)
            {
                BitArray s = (BitArray)symSet[i];
                gen.Append("\t\t|" + i + " -> let t = [|");
                int j = 0;
                foreach (Symbol sym in tab.terminals)
                {
                    if (s[sym.n]) gen.Append("x.T;"); else gen.Append("x.x;");
                    ++j;
                    if (j % 4 == 0) gen.Append(" ");
                }
                if (i == symSet.Count - 1) gen.AppendLine("x.x|] in x.Find(b,t)");
                else gen.AppendLine("x.x|] in x.Find(b,t)");
            }
        }

        void OpenGen(bool backUp)
        {
            try
            {
                string fn = Path.Combine(tab.outDir, "Parser.fs");
                if (File.Exists(fn) && backUp) File.Copy(fn, fn + ".old", true);
                gen2 = new StreamWriter(new FileStream(fn, FileMode.Create));
            }
            catch (IOException)
            {
                throw new FatalError("Cannot generate parser file");
            }
        }

        public void WriteParser()
        {
            StringWriter obj = new StringWriter();
            int initpos = buffer.Pos;
            int oldPos = buffer.Pos;  // Pos is modified by CopySourcePart
            symSet.Add(tab.allSyncSets);
            string fr = Path.Combine(tab.srcDir, "Parser.frame");
            if (!File.Exists(fr))
            {
                if (tab.frameDir != null) fr = tab.frameDir.Trim() + Path.DirectorySeparatorChar + "Parser.frame";
                if (!File.Exists(fr)) throw new FatalError("Cannot find Parser.frame");
            }
            try
            {
                fram = new FileStream(fr, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException)
            {
                throw new FatalError("Cannot open Parser.frame.");
            }
            OpenGen(true);
            err = new StringWriter();
            foreach (Symbol sym in tab.terminals) GenErrorMsg(tErr, sym);

            CopyFramePart("-->begin");
            if (!tab.srcName.ToLower().EndsWith("coco.atg"))
            {
                gen2.Close(); OpenGen(false);
            }
            CopyFramePart("-->namespace");
            /* AW open namespace, if it exists */
            if (tab.nsName != null && tab.nsName.Length > 0)
            {
                gen.AppendLine("namespace " + tab.nsName);
                gen.AppendLine();
            }

            if (usingPos != null) { CopySourcePart(usingPos, 0); gen.AppendLine(); }

            CopyFramePart("-->errors");
            int posicao = gen.Length;

            CopyFramePart("-->constants1");
            GenTokens();
            GenPragmas(); /* ML 2005/09/23 write the pragma kinds */
            CopyFramePart("-->variables"); CopySourcePart(tab.semDeclPos1, 0);
            CopyFramePart("-->initvariables"); CopySourcePart(tab.semDeclPos2, 0);
            CopyFramePart("-->constants2");
            GenTokens2(); /*initialize tokens*/
            GenPragmas2();
            CopyFramePart("-->declarations"); CopySourcePart(tab.semDeclPos, 0);
            CopyFramePart("-->pragmas"); GenCodePragmas();
            CopyFramePart("-->productions"); GenProductions();
            CopyFramePart("-->parseRoot"); gen.AppendLine("\t\tx." + tab.gramSy.name + "();");
            CopyFramePart("-->initialization"); InitSets();
            gen.Insert(posicao, err.ToString());
            CopyFramePart("$$$");

            if (tab.nsName != null && tab.nsName.Length > 0) gen.Append("//End namespace");
            buffer.Pos = oldPos;
            gen2.Write(gen.ToString());
            gen2.Close();
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

        public ParserOutput(Tab tab, Errors errors, TextWriter trace, Buffer buffer, Position uspos)
        {
            this.tab = tab;
            this.errors = errors;
            this.trace = trace;
            this.buffer = buffer;
            this.usingPos = uspos;
            errorNr = -1;
        }
    } // end ParserOutput
} // end namespace