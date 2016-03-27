using System;
using System.Collections;
using System.IO;

namespace CocoR
{
    public class ScannerOutput
    {
        public StreamWriter gen;
        public Tab tab;
        public bool ignoreCase;
        public bool hasCtxMoves;
        public State firstState;
        public Comment firstComment;
        private Hashtable keywords = new Hashtable();

        public ScannerOutput(StreamWriter gen, Tab tab, bool ignoreCase, bool hasCtxMoves, State firstState, Comment firstComment)
        {
            this.gen = gen;
            this.tab = tab;
            this.ignoreCase = ignoreCase;
            this.hasCtxMoves = hasCtxMoves;
            this.firstState = firstState;
            this.firstComment = firstComment;
            keywords.Add("-->namespace", GenNamespace());
            keywords.Add("-->declarations", GenDeclarations());
            keywords.Add("-->initialization", GenInitialization());
            keywords.Add("-->casing1", GenCasing1());
            keywords.Add("-->casing2", GenCasing2());
            keywords.Add("-->comments", GenComments());
            keywords.Add("-->literals", GenLiterals());
            keywords.Add("-->scan3", GenScan3());
            keywords.Add("-->scan1", GenScan1());
            keywords.Add("-->scan2", GenScan2());
            keywords.Add("$$$", GenDollarDollarDollar());
        }

        public void dispatch(String cmd)
        {
            if (keywords.Contains(cmd))
                gen.Write(keywords[cmd]);
            else
                Console.WriteLine(String.Format("Undefined insertion point {0}.", cmd)); // pf: should be an error!
        }

        //---------- Output primitives
        private string Ch(char ch)
        {
            if (ch < ' ' || ch >= 127 || ch == '\'' || ch == '\\') return Convert.ToString((int)ch);
            else return String.Format("Char.code '{0}'", ch);
        }

        private string Ch(int ch)
        {
            if (ch < ' ' || ch >= 127 || ch == '\'' || ch == '\\') return Convert.ToString(ch);
            else return String.Format("{0}", ch);
        }

        private string ChCond(char ch)
        {
            return String.Format("x.ch = {0}", Ch(ch));
        }

        private void PutRange(CharSet s)
        {
            for (CharSet.Range r = s.head; r != null; r = r.next)
            {
                if (r.from == r.to) { gen.Write("x.ch = " + Ch(r.from)); }
                else if (r.from == 0) { gen.Write("x.ch <= " + Ch(r.to)); }
                else { gen.Write("x.ch >= " + Ch(r.from) + " && x.ch <= " + Ch(r.to)); }
                if (r.next != null) gen.Write(" || ");
            }
        }

        private void PutRange(BitArray s)
        {
            int[] lo = new int[32];
            int[] hi = new int[32];
            // fill lo and hi
            int max = CharClass.charSetSize;

            int top = -1;
            int i = 0;
            while (i < max)
            {
                if (s[i])
                {
                    top++; lo[top] = i; i++;
                    while (i < max && s[i]) i++;
                    hi[top] = i - 1;
                }
                else i++;
            }
            // print ranges
            if (top == 1 && lo[0] == 0 && hi[1] == max - 1 && hi[0] + 2 == lo[1])
            {
                BitArray s1 = new BitArray(max); s1[hi[0] + 1] = true;
                gen.Write("!"); PutRange(s1); gen.Write(" && x.ch <> x.buffer.EOF");
            }
            else {
                gen.Write("(");
                for (i = 0; i <= top; i++)
                {
                    if (hi[i] == lo[i]) gen.Write("x.ch = {0}", Ch((char)lo[i]));
                    else if (lo[i] == 0) gen.Write("x.ch <= {0}", Ch((char)hi[i]));
                    else gen.Write("x.ch >= {0} && x.ch <= {1}", Ch((char)lo[i]), Ch((char)hi[i]));
                    if (i < top) gen.Write(" || ");
                }
                gen.Write(")");
            }
        }

        //------------------------ scanner generation ----------------------

        void GenComBody(Comment com)
        {
            gen.WriteLine("\t\t\t try ");
            gen.WriteLine("\t\t\t\t while !more do ");
            gen.WriteLine("\t\t\t\t\t if ({0}) ", ChCond(com.stop[0]));
            gen.WriteLine("\t\t\t\t\t then ( ");

            if (com.stop.Length == 1)
            {
                gen.WriteLine("\t\t\t\t\t\t level := post_dec !level;"); ;
                gen.WriteLine("\t\t\t\t\t\t if !level = 0 then( ");
                gen.WriteLine("\t\t\t\t\t\t\t x.oldEols <- x.line - line0;  ");
                gen.WriteLine("\t\t\t\t\t\t\t x.NextCh(); ");
                gen.WriteLine("\t\t\t\t\t\t\t result := true; ");
                gen.WriteLine("\t\t\t\t\t\t\t more:= false )");
            }
            else {
                gen.WriteLine("\t\t\t\t\tx.NextCh();");
                gen.WriteLine("\t\t\t\t\tif ({0}) then (", ChCond(com.stop[1]));
                gen.WriteLine("\t\t\t\t\t\t level := post_dec !level;");
                gen.WriteLine("\t\t\t\t\t\t if !level = 0 then( ");
                gen.WriteLine("\t\t\t\t\t\t\t x.oldEols <- x.line - line0;  ");
                gen.WriteLine("\t\t\t\t\t\t\t x.NextCh(); ");
                gen.WriteLine("\t\t\t\t\t\t\t result := true; ");
                gen.WriteLine("\t\t\t\t\t\t\t more:= false )");
                gen.WriteLine("\t\t\t\t\telse x.NextCh();");
                gen.WriteLine("\t\t\t\t\t)");
            }

            if (com.nested)
            {
                gen.WriteLine("\t\t\t\t\t ) else if ({0}) then (", ChCond(com.start[0]));
                if (com.start.Length == 1)
                    gen.WriteLine("\t\t\t\t\tlevel := post_inc !level; x.NextCh();");
                else {
                    gen.WriteLine("\t\t\t\t\tx.NextCh();");
                    gen.WriteLine("\t\t\t\t\tif ({0}) then (", ChCond(com.start[1]));
                    gen.WriteLine("\t\t\t\t\t\t level := post_inc !level; x.NextCh();");
                    gen.WriteLine("\t\t\t\t\t)");
                }
            }
            gen.WriteLine("\t\t\t\t) else x.NextCh();");
            gen.WriteLine("\t\t\t\t done;");
            gen.WriteLine("\t\t\t\t !result;");

            gen.WriteLine("\t\t\twith");
            gen.WriteLine("\t\t\t\tEnd_of_file -> false )");
        }

        void GenComment(Comment com, int i)
        {
            gen.WriteLine();
            gen.WriteLine("\tmember x.Comment{0}() =", i);
            gen.WriteLine("\t\tlet level = ref 1 and line0 = x.line and lineStart0 = x.lineStart and more = ref true and result = ref false in");
            if (com.start.Length == 1)
            {
                gen.WriteLine("\t\tx.NextCh();");
                GenComBody(com);
            }
            else {
                gen.WriteLine("\t\tx.NextCh();");
                gen.WriteLine("\t\tif ({0}) \n\t\tthen (", ChCond(com.start[1]));
                gen.WriteLine("\t\t\tx.NextCh();");
                GenComBody(com);
                gen.WriteLine("\t\t else (");
                gen.WriteLine("\t\t\tif x.ch = Char.code x.EOL then (");
                gen.WriteLine("\t\t\t level := post_dec !level;");
                gen.WriteLine("\t\t\tx.lineStart <- lineStart0 );");

                gen.WriteLine("\t\t\t x.buffer.pos <- x.buffer.pos - 2;");
                gen.WriteLine("\t\t\t  seek_in (x.buffer.stream) (x.buffer.pos+1);");
                gen.WriteLine("\t\t\t x.NextCh();");
                gen.WriteLine("\t\t\t false )");
            }
            gen.WriteLine("\t");
        }

        string SymName(Symbol sym)
        {
            if (Char.IsLetter(sym.name[0]))
            { // real name value is stored in Tab.literals
                foreach (DictionaryEntry e in tab.literals)
                    if ((Symbol)e.Value == sym) return (string)e.Key;
            }
            return sym.name;
        }

        void GenXLiterals()
        {
            if (ignoreCase)
            {
                gen.WriteLine("\t\tmatch lit.ToLower() with \n");
            }
            else {
                gen.WriteLine("\t\tmatch lit with\n");
            }
            foreach (Symbol sym in tab.terminals)
            {
                if (sym.tokenKind == Symbol.litToken)
                {
                    string name = SymName(sym);
                    if (ignoreCase) name = name.ToLower();
                    // sym.name stores literals with quotes, e.g. "\"Literal\""
                    gen.WriteLine("\t\t\t| {0} -> {1} \n", name, sym.n);
                }
            }
            gen.WriteLine("\t\t\t| _ -> def ");
        }

        void WriteState(State state)
        {
            Symbol endOf = state.endOf;
            gen.WriteLine("\t\t| {0} -> ", state.nr);
            bool ctxEnd = state.ctx;
            for (Action action = state.firstAction; action != null; action = action.next)
            {
                if (action == state.firstAction) gen.Write("\t\t\t\tif (");
                else gen.Write("\t\t\t\telse if (");
                if (action.typ == Node.chr)
                {
                    gen.Write(ChCond((char)action.sym));
                }
                else
                {
                    //Convert Charset to BitArray
                    CharSet teste2 = tab.CharClassSet(action.sym);
                    bool[] cg = new bool[256];
                    for (int i = 0; i < 256; i++) cg[i] = teste2[i];
                    BitArray cc = new BitArray(cg);
                    PutRange(cc);
                }
                gen.Write(") then (");
                if (action.tc == Node.contextTrans)
                {
                    gen.Write("apx:=apx+1; "); ctxEnd = false;
                }
                else if (state.ctx)
                    gen.Write("apx := 0; ");
                gen.Write("x.AddCh(); x.resolveKind {0};", action.target.state.nr);
                gen.WriteLine(")");
            }
            if (state.firstAction == null)
                gen.Write("\t\t\t\t(");
            else
                gen.Write("\t\t\t\telse (");
            if (ctxEnd)
            { // final context state: cut appendix
                gen.WriteLine();
                gen.WriteLine("\t\t\t\t\ttlen := tlen- apx;");
                gen.WriteLine("\t\t\t\t\tpos := pos - apx - 1; line = t.line;");
                gen.WriteLine("\t\t\t\t\tbuffer.Pos := pos + 1; x.NextCh();");
                gen.Write("\t\t\t\t\t");
            }
            if (endOf == null)
            {
                gen.WriteLine(" x.noSym )");
            }
            else {
                if (endOf.tokenKind == Symbol.classLitToken)
                {
                    gen.Write("x.checkLiteral (Buffer.contents x.tval) ");
                    gen.WriteLine(" {0} )", endOf.n);
                }
                else {
                    gen.Write(" {0} ", endOf.n);
                    gen.WriteLine(")");
                }
            }
        }

        void FillStartTab(int[] startTab)
        {
            for (Action action = firstState.firstAction; action != null; action = action.next)
            {
                int targetState = action.target.state.nr;
                if (action.typ == Node.chr) startTab[action.sym] = targetState;
                else {
                    //Convert the charset to a bitarray
                    CharSet teste2 = tab.CharClassSet(action.sym);
                    bool[] cg = new bool[256];
                    for (int i = 0; i < 256; i++) cg[i] = teste2[i];
                    BitArray cc = new BitArray(cg);
                    for (int i = 0; i < cc.Count; i++)
                        if (cc[i]) startTab[i] = targetState;
                }
            }
        }

        String GenNamespace()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            if (tab.nsName != null && tab.nsName.Length > 0)
            {
                gen.Write("namespace ");
                gen.Write(tab.nsName);
                gen.Write(" {");
            }
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenDeclarations()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            int[] startTab = new int[CharClass.charSetSize];
            FillStartTab(startTab);
            gen.WriteLine("charSetSize = {0};", CharClass.charSetSize);
            gen.WriteLine("\t\tmaxT = {0};", tab.terminals.Count - 1);
            gen.WriteLine("\t\tnoSym = {0};", tab.noSym.n);
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        void WriteStart()//Write Start Tab
        {
            for (Action action = firstState.firstAction; action != null; action = action.next)
            {
                int targetState = action.target.state.nr;
                if (action.typ == Node.chr)
                {
                    gen.WriteLine("\t\tx.start.Add(" + action.sym + "," + targetState + "); ");
                }
                else
                {
                    CharSet s = tab.CharClassSet(action.sym);
                    for (CharSet.Range r = s.head; r != null; r = r.next)
                    {
                        gen.Write("\t\tfor i = " + r.from + " to " + r.to + " do ");
                        gen.WriteLine("\t\tx.start.Add(i," + targetState + "); done;");
                    }
                }
            }
            gen.WriteLine("\t\tx.start.Add(65535+1, -1);");
        }

        String GenInitialization()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            Comment com = firstComment;

            StreamReader reader = new StreamReader(insertion);

            WriteStart();
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenCasing1()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            if (ignoreCase)
            {
                gen.WriteLine(";");
                gen.Write("\t\tif (x.ch <> x.buffer.EOF ) then x.ch <- Char.code (char.ToLower(Char.chr x.ch))");
            }
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenCasing2()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            if (ignoreCase) gen.Write("	ignore(x.tval.Append(char.ToLower(Char.chr x.ch))); "); else gen.Write("ignore (x.tval.Append(Char.chr x.ch));");
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenComments()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            Comment com = firstComment; int i = 0;
            while (com != null)
            {
                GenComment(com, i);
                com = com.next; i++;
            }
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenLiterals()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            GenXLiterals();
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenScan1()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            Comment com = firstComment; int i = 0;
            if (firstComment != null)
            {
                gen.Write("\t\tif (");
                while (com != null)
                {
                    gen.Write(ChCond(com.start[0]));
                    gen.Write(" && x.Comment{0}()", i);
                    if (com.next != null) gen.Write(" || ");
                    com = com.next; i++;
                }
                gen.Write(") then x.NextToken() ");
                gen.WriteLine("else");
            }
            if (hasCtxMoves) { gen.WriteLine(); gen.Write("\t\tlet apx = ref 0"); } // pdt
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenScan2()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            for (State state = firstState.next; state != null; state = state.next)
                WriteState(state);
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenScan3()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            CharSet nt = new CharSet();
            CharSet tt = new CharSet();
            Comment com = firstComment;

            StreamReader reader = new StreamReader(insertion);

            gen.Write(" ");
            if (tab.ignored.Elements() > 0) { PutRange(tab.ignored); } else { gen.Write("false"); }
            gen.Write(" ");
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }

        String GenDollarDollarDollar()
        {
            MemoryStream insertion = new MemoryStream();
            StreamWriter oldGen = gen;
            gen = new StreamWriter(insertion);
            StreamReader reader = new StreamReader(insertion);
            if (tab.nsName != null && tab.nsName.Length > 0) gen.Write("}");
            gen.Flush();
            insertion.Seek(0, SeekOrigin.Begin);
            gen = oldGen;
            return reader.ReadToEnd();
        }
    }// End ScannerOutput
}//End Namespace