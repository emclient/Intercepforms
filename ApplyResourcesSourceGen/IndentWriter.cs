using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ApplyResourcesSourceGen
{
	class IndentWriter : StringWriter
	{
		private const string INDENT = "\t";
		public int Level { get; private set; }
		private bool indentNeeded = true;

		public IndentWriter(StringBuilder sb) : base(sb)
		{

		}

		public override void WriteLine()
		{
			WriteIndent();
			base.WriteLine();
			indentNeeded = true;
		}

		public override void WriteLine(string value)
		{
			WriteIndent();
			base.WriteLine(value);
			indentNeeded = true;
		}

		public override void Write(string value)
		{
			WriteIndent();
			base.Write(value);
		}

		public void StartBlock()
		{
			WriteLine("{");
			Level++;
		}

		public void EndBlock()
		{
			Level--;
			if (Level < 0) throw new InvalidOperationException();
			WriteLine("}");
		}

		public IDisposable WriteBlock()
		{
			StartBlock();
			return new BlockDisposable(this);
		}

		private void WriteIndent()
		{
			if (indentNeeded)
			{
				indentNeeded = false;
				for (var i = 0; i < Level; i++) base.Write(INDENT);
			}
		}

		private class BlockDisposable : IDisposable
		{
			private readonly IndentWriter writer;

			public BlockDisposable(IndentWriter writer)
			{
				this.writer = writer;
			}

			public void Dispose()
			{
				writer.EndBlock();
			}
		}
	}
}
