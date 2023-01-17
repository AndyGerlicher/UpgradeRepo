using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpgradeRepo
{
    internal class ProjFileInfo : FileSystemInfo
    {
        private readonly object _lockObject = new object();
        private Encoding? _encoding = null;
        private bool _endsWithNewLine;

        public ProjFileInfo(string path)
        {
            this.FullPath = path;
        }
        public override void Delete()
        {
            throw new NotImplementedException();
        }

        public override bool Exists => File.Exists(FullPath);

        public override string Name => base.FullName;
        
        public Encoding Encoding
        {
            get
            {
                EnsureReadFile();
                return _encoding!;
            }
        }

        public bool EndsWithNewLine
        {
            get
            {
                EnsureReadFile();
                return _endsWithNewLine;
            }
        }

        private void EnsureReadFile()
        {
            if (_encoding != null) return;
            lock (_lockObject)
            {
                if (_encoding == null)
                {
                    var (encoding, endsWithNewLine) = ReadFile(FullPath);
                    _encoding = encoding;
                    _endsWithNewLine = endsWithNewLine;
                }
            }
        }

        private (Encoding, bool) ReadFile(string path)
        {
            if (path.Contains(@"Src\Extensions\ICMextension\Framework.IcM\Framework.IcM.csproj"))
            {
                Debugger.Break();
            }
            Byte[] bytes = File.ReadAllBytes(path);
            Encoding? encoding = null;

            // Test UTF8 with BOM. This check can easily be copied and adapted
            // to detect many other encodings that use BOMs.
            UTF8Encoding encUtf8Bom = new UTF8Encoding(true, true);
            Boolean couldBeUtf8 = true;
            Byte[] preamble = encUtf8Bom.GetPreamble();
            Int32 prLen = preamble.Length;
            if (bytes.Length >= prLen && preamble.SequenceEqual(bytes.Take(prLen)))
            {
                // UTF8 BOM found; use encUtf8Bom to decode.
                try
                {
                    encoding = encUtf8Bom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                    couldBeUtf8 = false;
                }
            }
            // use boolean to skip this if it's already confirmed as incorrect UTF-8 decoding.
            if (couldBeUtf8 && encoding == null)
            {
                // test UTF-8 on strict encoding rules. Note that on pure ASCII this will
                // succeed as well, since valid ASCII is automatically valid UTF-8.
                UTF8Encoding encUtf8NoBom = new UTF8Encoding(false, true);
                try
                {
                    encoding = encUtf8NoBom;
                }
                catch (ArgumentException)
                {
                    // Confirmed as not UTF-8!
                }
            }
            // fall back to default ANSI encoding.
            encoding ??= Encoding.GetEncoding(1252);

            bool endsWithNewLine = bytes.EndsWith(encoding.GetBytes(Environment.NewLine));

            return (encoding, endsWithNewLine);
        }
    }
}
