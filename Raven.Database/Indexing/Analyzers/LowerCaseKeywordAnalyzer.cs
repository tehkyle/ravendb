//-----------------------------------------------------------------------
// <copyright file="LowerCaseAnalyzer.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using Lucene.Net.Analysis;

namespace Raven.Database.Indexing
{
    public class LowerCaseKeywordAnalyzer : Analyzer
    {
        public override TokenStream ReusableTokenStream(string fieldName, TextReader reader)
        {
            var previousTokenStream = (LowerCaseKeywordTokenizer)PreviousTokenStream;
            if (previousTokenStream == null)
            {
                var newStream = TokenStream(fieldName, reader);
                PreviousTokenStream = newStream;
                return newStream;
            }
            previousTokenStream.Reset(reader);
            return previousTokenStream;
        }

        public override TokenStream TokenStream(string fieldName, TextReader reader)
        {
            var res = new LowerCaseKeywordTokenizer(reader);
            PreviousTokenStream = res;
            return res;
        }
    }
}
