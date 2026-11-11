using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace BackRooms.UI
{
    /// <summary>
    /// Grava um vídeo AVI com imagem em Motion JPEG e som PCM de 16 bits — um formato simples
    /// o bastante para ser escrito à mão, sem codificador externo, e que qualquer tocador abre.
    ///
    /// Quem chama entrega quadros crus (RGBA) e blocos de som; uma thread própria comprime
    /// cada quadro em JPEG e escreve tudo no arquivo, na ordem em que chegou. Assim o jogo não
    /// trava esperando disco nem compressão. O cabeçalho é reescrito no fim, com os totais.
    /// </summary>
    public sealed class AviWriter
    {
        const int HeaderBytes = 12 + 12 + 64 + 12 + 64 + 48 + AudioListBytes + 12;
        const int AudioListBytes = 12 + 64 + 24;
        const long SafeLimit = 1900L * 1024 * 1024;

        enum Kind { Frame, Repeat, Audio, End }

        struct Job
        {
            public Kind Kind;
            public byte[] Bytes;
            public float[] Samples;
            public int Count;
        }

        struct IndexEntry
        {
            public bool Audio;
            public bool Key;
            public uint Offset;
            public uint Size;
        }

        readonly string _path;
        readonly int _width, _height, _fps, _quality;
        readonly int _audioRate, _audioChannels;
        readonly BlockingCollection<Job> _jobs = new BlockingCollection<Job>(new ConcurrentQueue<Job>());
        readonly ConcurrentBag<byte[]> _framePool = new ConcurrentBag<byte[]>();
        readonly ConcurrentBag<float[]> _audioPool = new ConcurrentBag<float[]>();
        readonly List<IndexEntry> _index = new List<IndexEntry>();
        readonly Thread _worker;

        FileStream _stream;
        BinaryWriter _writer;
        long _moviStart;
        long _audioBytes;
        int _frames;
        int _maxFrameBytes;
        volatile bool _full;
        volatile string _error;
        byte[] _pcm = new byte[0];

        /// <summary>Quadros já escritos no arquivo (contando os repetidos).</summary>
        public int Frames => _frames;

        /// <summary>Quadros entregues, escritos ou ainda na fila.</summary>
        public int FramesQueued { get; private set; }

        /// <summary>Verdadeiro quando o arquivo chegou perto do limite do formato (2 GB).</summary>
        public bool Full => _full;

        /// <summary>Mensagem do erro que interrompeu a gravação, ou nulo.</summary>
        public string Error => _error;

        public string Path => _path;

        public AviWriter(string path, int width, int height, int fps, int quality, int audioRate, int audioChannels)
        {
            _path = path;
            _width = width;
            _height = height;
            _fps = Mathf.Max(1, fps);
            _quality = Mathf.Clamp(quality, 1, 100);
            _audioRate = audioRate;
            _audioChannels = Mathf.Clamp(audioChannels, 1, 2);

            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
            _writer = new BinaryWriter(_stream);
            WriteHeader();
            _moviStart = _stream.Position - 4;

            _worker = new Thread(Work) { Name = "Gravacao AVI", IsBackground = true };
            _worker.Start();
        }

        /// <summary>Um vetor do tamanho de um quadro RGBA, reaproveitado entre quadros.</summary>
        public byte[] RentFrame()
        {
            int size = _width * _height * 4;
            return _framePool.TryTake(out byte[] buffer) && buffer.Length == size ? buffer : new byte[size];
        }

        /// <summary>Entrega um quadro cru (RGBA, de baixo para cima). O vetor volta para o reuso depois de comprimido.</summary>
        public void AddFrame(byte[] rgba)
        {
            if (_jobs.IsAddingCompleted) return;
            FramesQueued++;
            _jobs.Add(new Job { Kind = Kind.Frame, Bytes = rgba });
        }

        /// <summary>Repete o quadro anterior (o jogo rodou mais devagar que o vídeo). Não ocupa espaço.</summary>
        public void AddRepeat()
        {
            if (_jobs.IsAddingCompleted) return;
            FramesQueued++;
            _jobs.Add(new Job { Kind = Kind.Repeat });
        }

        /// <summary>Entrega um bloco de som. Pode ser chamado da thread de áudio.</summary>
        public void AddAudio(float[] samples, int channels)
        {
            if (_jobs.IsAddingCompleted || _audioRate <= 0 || channels <= 0) return;

            // Guarda sempre no número de canais do arquivo: sobra de canais é descartada, mono é dobrado.
            int frames = samples.Length / channels;
            int count = frames * _audioChannels;
            if (!_audioPool.TryTake(out float[] copy) || copy.Length < count) copy = new float[count];

            for (int i = 0; i < frames; i++)
                for (int c = 0; c < _audioChannels; c++)
                    copy[i * _audioChannels + c] = samples[i * channels + Mathf.Min(c, channels - 1)];

            try { _jobs.Add(new Job { Kind = Kind.Audio, Samples = copy, Count = count }); }
            catch (InvalidOperationException) { }
        }

        /// <summary>Escreve o que falta, fecha o arquivo e devolve quando terminou.</summary>
        public void Finish()
        {
            if (!_jobs.IsAddingCompleted)
            {
                _jobs.Add(new Job { Kind = Kind.End });
                _jobs.CompleteAdding();
            }

            _worker.Join();
        }

        void Work()
        {
            try
            {
                foreach (Job job in _jobs.GetConsumingEnumerable())
                {
                    if (job.Kind == Kind.End) break;
                    if (_full) continue;

                    switch (job.Kind)
                    {
                        case Kind.Frame:
                            byte[] jpeg = ImageConversion.EncodeArrayToJPG(job.Bytes, GraphicsFormat.R8G8B8A8_SRGB, (uint)_width, (uint)_height, 0, _quality);
                            _framePool.Add(job.Bytes);
                            WriteChunk(false, jpeg, jpeg.Length, true);
                            _frames++;
                            _maxFrameBytes = Mathf.Max(_maxFrameBytes, jpeg.Length);
                            break;

                        case Kind.Repeat:
                            WriteChunk(false, null, 0, false);
                            _frames++;
                            break;

                        case Kind.Audio:
                            int bytes = job.Count * 2;
                            if (_pcm.Length < bytes) _pcm = new byte[bytes];

                            for (int i = 0; i < job.Count; i++)
                            {
                                short value = (short)Mathf.RoundToInt(Mathf.Clamp(job.Samples[i], -1f, 1f) * 32767f);
                                _pcm[i * 2] = (byte)value;
                                _pcm[i * 2 + 1] = (byte)(value >> 8);
                            }

                            _audioPool.Add(job.Samples);
                            WriteChunk(true, _pcm, bytes, true);
                            _audioBytes += bytes;
                            break;
                    }

                    if (_stream.Position > SafeLimit) _full = true;
                }

                Close();
            }
            catch (Exception e)
            {
                _error = e.Message;
                try { _stream.Dispose(); } catch (Exception) { }
            }
        }

        void WriteChunk(bool audio, byte[] data, int size, bool key)
        {
            _index.Add(new IndexEntry { Audio = audio, Key = key, Offset = (uint)(_stream.Position - _moviStart), Size = (uint)size });

            WriteTag(audio ? "01wb" : "00dc");
            _writer.Write(size);
            if (size > 0) _writer.Write(data, 0, size);
            if ((size & 1) != 0) _writer.Write((byte)0);
        }

        void Close()
        {
            long moviEnd = _stream.Position;

            // Índice: onde está cada pedaço, para o tocador poder avançar e voltar.
            WriteTag("idx1");
            _writer.Write(_index.Count * 16);
            foreach (IndexEntry entry in _index)
            {
                WriteTag(entry.Audio ? "01wb" : "00dc");
                _writer.Write(entry.Key ? 0x10 : 0);
                _writer.Write(entry.Offset);
                _writer.Write(entry.Size);
            }

            long end = _stream.Position;

            // Agora que os totais são conhecidos, o cabeçalho é escrito de novo por cima.
            _stream.Position = 0;
            WriteHeader((uint)(end - 8), (uint)(moviEnd - _moviStart));
            _writer.Flush();
            _stream.Dispose();
        }

        void WriteHeader(uint riffSize = 0, uint moviSize = 0)
        {
            bool audio = _audioBytes > 0;
            int blockAlign = _audioChannels * 2;

            WriteTag("RIFF");
            _writer.Write(riffSize);
            WriteTag("AVI ");

            WriteTag("LIST");
            _writer.Write(HeaderBytes - 12 - 8 - 12);
            WriteTag("hdrl");

            WriteTag("avih");
            _writer.Write(56);
            _writer.Write(1000000 / _fps);                       // microssegundos por quadro
            _writer.Write(_maxFrameBytes * _fps + _audioRate * blockAlign);
            _writer.Write(0);
            _writer.Write(0x10 | 0x100);                         // tem índice, intercalado
            _writer.Write(_frames);
            _writer.Write(0);
            _writer.Write(audio ? 2 : 1);
            _writer.Write(_maxFrameBytes);
            _writer.Write(_width);
            _writer.Write(_height);
            _writer.Write(0); _writer.Write(0); _writer.Write(0); _writer.Write(0);

            WriteTag("LIST");
            _writer.Write(4 + 64 + 48);
            WriteTag("strl");

            WriteTag("strh");
            _writer.Write(56);
            WriteTag("vids");
            WriteTag("MJPG");
            _writer.Write(0);
            _writer.Write(0);
            _writer.Write(0);
            _writer.Write(1);                                    // escala
            _writer.Write(_fps);                                 // taxa: quadros por segundo
            _writer.Write(0);
            _writer.Write(_frames);
            _writer.Write(_maxFrameBytes);
            _writer.Write(-1);
            _writer.Write(0);
            _writer.Write((short)0); _writer.Write((short)0); _writer.Write((short)_width); _writer.Write((short)_height);

            WriteTag("strf");
            _writer.Write(40);
            _writer.Write(40);
            _writer.Write(_width);
            _writer.Write(_height);
            _writer.Write((short)1);
            _writer.Write((short)24);
            WriteTag("MJPG");
            _writer.Write(_width * _height * 3);
            _writer.Write(0); _writer.Write(0); _writer.Write(0); _writer.Write(0);

            if (audio)
            {
                WriteTag("LIST");
                _writer.Write(4 + 64 + 24);
                WriteTag("strl");

                WriteTag("strh");
                _writer.Write(56);
                WriteTag("auds");
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(0);
                _writer.Write(blockAlign);                       // escala
                _writer.Write(_audioRate * blockAlign);          // taxa: bytes por segundo
                _writer.Write(0);
                _writer.Write((int)(_audioBytes / blockAlign));
                _writer.Write(_audioRate * blockAlign);
                _writer.Write(-1);
                _writer.Write(blockAlign);
                _writer.Write(0L);

                WriteTag("strf");
                _writer.Write(16);
                _writer.Write((short)1);                         // PCM
                _writer.Write((short)_audioChannels);
                _writer.Write(_audioRate);
                _writer.Write(_audioRate * blockAlign);
                _writer.Write((short)blockAlign);
                _writer.Write((short)16);
            }
            else
            {
                // Sem som (ou ainda sem saber se haverá): o espaço da faixa de áudio fica reservado.
                WriteTag("JUNK");
                _writer.Write(AudioListBytes - 8);
                _writer.Write(new byte[AudioListBytes - 8]);
            }

            WriteTag("LIST");
            _writer.Write(moviSize);
            WriteTag("movi");
        }

        void WriteTag(string tag)
        {
            for (int i = 0; i < 4; i++) _writer.Write((byte)tag[i]);
        }
    }
}
