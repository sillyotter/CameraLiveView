using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    public class DelimitedStreamReader : IDisposable
    {
        private readonly Stream _s;
        private readonly IBufferProvider _prov;
        private readonly byte[] _header;
        private readonly byte[] _footer;
        private const int DefaultBufferSize = 32*1024; // This might need to be bigger, or perhaps smaller. depending on use

        private byte[] _buffer;
        private readonly byte[] _tempBuf;
        private int _postEndIndex = 0;
        private int _readCount = 1;
        private int _headerIndex = -1;
        private int _frameLength = 2;

        public DelimitedStreamReader(Stream s, IBufferProvider prov, byte[] header, byte[] footer)
        {
            _s = s;
            _prov = prov;
            _header = header;
            _footer = footer;
            _buffer = _prov.TakeBuffer(DefaultBufferSize);
            _tempBuf = _prov.TakeBuffer(DefaultBufferSize);
        }

        public async Task<Tuple<byte[], int>> ReadJpegBytesAsync(CancellationToken tok)
        {
            _readCount = await _s.ReadAsync(_tempBuf, 0, _tempBuf.Length, tok).ConfigureAwait(false);

            do
            {
                if (_buffer.Length - _postEndIndex < _readCount)
                {
                    var nb = _prov.TakeBuffer(_buffer.Length*2);
                    Buffer.BlockCopy(_buffer, 0, nb, 0, _buffer.Length);
                    var temp = _buffer;
                    _buffer = nb;
                    _prov.ReturnBuffer(temp);
                }

                Buffer.BlockCopy(_tempBuf, 0, _buffer, _postEndIndex, _readCount);
                _postEndIndex += _readCount;

                if (_headerIndex == -1)
                {
                    _headerIndex = _buffer.Find(_postEndIndex, _header, 0);
                }

                // im making the assumption that frames dont vary in size by that much, so rather than 
                // search from headerindex on, skip forward a bit.  here, i skip forward 3/4 the previous framelength, 
                // which shouldnt miss the end of the next frame, but which should reduce the amount
                // of searching we do.
                var footerIndex = _buffer.Find(_postEndIndex, _footer, _headerIndex + (int) (_frameLength*.75));

                if (_headerIndex != -1 && footerIndex != -1)
                {
                    var postFooterIndex = footerIndex + _footer.Length;
                    _frameLength = postFooterIndex - _headerIndex;

                    var frame = _prov.TakeBuffer(_frameLength);
                    // instead of just allocating a big buffer for each frame, which may 
                    // in time resulting in fragmenting the LOH, we will use this buffer manager
                    // which keeps a pool of reusable buffers around for ever, allowing us to ignore
                    // fragmentation and loh GC pauses.

                    Buffer.BlockCopy(_buffer, _headerIndex, frame, 0, _frameLength);

                    var tailLength = _postEndIndex - postFooterIndex;
                    Buffer.BlockCopy(_buffer, postFooterIndex, _buffer, 0, tailLength);
                    _postEndIndex = tailLength;
                    _headerIndex = -1;

                    return Tuple.Create(frame, _frameLength);
                }

                _readCount = await _s.ReadAsync(_tempBuf, 0, _tempBuf.Length, tok).ConfigureAwait(false);

            } while (!tok.IsCancellationRequested && _readCount > 0);

            return null;
        }

        public void Dispose()
        {
            _prov.ReturnBuffer(_buffer);
            _prov.ReturnBuffer(_tempBuf);
        }
    }
}