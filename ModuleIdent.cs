﻿using BitFab.KW1281Test.Blocks;
using System;
using System.Collections.Generic;
using System.Text;

namespace BitFab.KW1281Test
{
    /// <summary>
    /// The info returned by the module to a ReadIdent block.
    /// </summary>
    internal class ModuleIdent
    {
        public ModuleIdent(IEnumerable<Block> blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks)
            {
                if (block is AsciiDataBlock asciiBlock)
                {
                    sb.Append(asciiBlock);
                }
                else
                {
                    Console.WriteLine($"ReadIdent returned block of type {block.GetType()}");
                }
            }
            Text = sb.ToString();
        }

        public string Text { get; }

        public override string ToString()
        {
            return Text;
        }
    }
}