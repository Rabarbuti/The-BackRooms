using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace BackRooms.UI
{
    /// <summary>
    /// Galeria das gravações (F9, <see cref="ScreenRecorder"/>): a tela "Recordings", aberta pelo menu
    /// inicial (<see cref="ConnectionMenu"/>) e pelo menu de pausa (<see cref="PauseMenu"/>). Lista os
    /// clipes da pasta do gravador, o mais recente primeiro, com data e hora, duração e tamanho; Play
    /// abre o arquivo no reprodutor do sistema, Open folder abre a pasta e Delete apaga, depois de
    /// confirmar. Sem clipes, diz como gravar.
    ///
    /// Montada em código com as peças do <see cref="MenuKit"/>, como uma camada por cima do menu
    /// que a criou. A duração vem do cabeçalho do AVI (<see cref="AviWriter.ReadInfo"/>), sem abrir
    /// o vídeo.
    /// </summary>
    public sealed class RecordingsScreen
    {
        // ---------------------------------------------------------------- Textos (a língua do jogo é o inglês)
        public const string ButtonText = "Recordings";
        const string TitleText = "RECORDINGS";
        const string FolderLabel = "Saved in ";
        const string EmptyText = "No recordings yet.\nPress R while playing to start recording, and again to stop.\nThe clip shows up here.";
        const string PlayText = "Play";
        const string DeleteText = "Delete";
        const string ConfirmText = "Delete this clip?";
        const string YesText = "Yes, delete";
        const string NoText = "No";
        const string OpenFolderText = "Open folder";
        const string BackText = "Back        [Esc]";
        const string RecordingNow = "recording now...";
        const string Unfinished = "unfinished";
        const string DeleteFailed = "Could not delete: ";
        const string OpenFailed = "Could not open: ";

        /// <summary>Classe dos rótulos da lista (os testes leem o que está desenhado).</summary>
        public const string RowClass = "gravacoes-linha";

        /// <summary>
        /// Para os testes: em vez de abrir o reprodutor ou o Explorer, entrega aqui o endereço
        /// (file:///...) que seria aberto.
        /// </summary>
        public static Action<string> OpenOverride;

        /// <summary>A última galeria aberta (testes).</summary>
        public static RecordingsScreen Current { get; private set; }

        public VisualElement Root { get; }
        public bool IsOpen => Root.style.display == DisplayStyle.Flex;

        /// <summary>Os clipes como estão na lista agora (testes).</summary>
        public IReadOnlyList<Clip> Clips => _clips;

        readonly ScrollView _list;
        readonly Label _folder;
        readonly Label _message;
        readonly List<Clip> _clips = new List<Clip>();
        string _confirming;

        /// <summary>Um clipe na pasta.</summary>
        public struct Clip
        {
            public string Path;
            public DateTime When;
            public long Bytes;
            public float Seconds;
            public bool Finished;
            public bool Recording;
        }

        public RecordingsScreen(VisualElement parent)
        {
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = 0; Root.style.right = 0; Root.style.top = 0; Root.style.bottom = 0;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.9f);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.display = DisplayStyle.None;
            parent.Add(Root);

            var panel = new VisualElement();
            panel.style.width = 940;
            panel.style.maxHeight = Length.Percent(90);
            panel.style.paddingLeft = 36; panel.style.paddingRight = 36;
            panel.style.paddingTop = 28; panel.style.paddingBottom = 28;
            // Opaco: por cima do menu de pausa, o texto de trás aparecia através do painel.
            Color solid = MenuKit.PanelColor;
            solid.a = 1f;
            panel.style.backgroundColor = solid;
            panel.style.borderTopWidth = 2; panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2; panel.style.borderRightWidth = 2;
            Color edge = MenuKit.Yellow * new Color(1f, 1f, 1f, 0.45f);
            panel.style.borderTopColor = edge; panel.style.borderBottomColor = edge;
            panel.style.borderLeftColor = edge; panel.style.borderRightColor = edge;
            MenuKit.Round(panel);
            Root.Add(panel);

            Label title = MenuKit.Text(TitleText, 32, MenuKit.Yellow);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 3;
            panel.Add(title);

            _folder = MenuKit.Text("", 14, MenuKit.Paper);
            _folder.style.opacity = 0.6f;
            _folder.style.marginBottom = 16;
            _folder.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_folder);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexShrink = 1f;
            _list.style.minHeight = 120;
            _list.style.maxHeight = 520;
            panel.Add(_list);

            _message = MenuKit.Text("", 15, MenuKit.Yellow);
            _message.style.marginTop = 8;
            _message.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_message);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 18;
            panel.Add(buttons);
            Button folder = MenuKit.MenuButton(OpenFolderText, OpenFolder);
            folder.style.width = 260; folder.style.marginRight = 14;
            buttons.Add(folder);
            Button back = MenuKit.MenuButton(BackText, Hide);
            back.style.width = 260;
            buttons.Add(back);
        }

        public void Show()
        {
            Current = this;
            Root.style.display = DisplayStyle.Flex;
            _confirming = null;
            _message.text = "";
            Refresh();
        }

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            _confirming = null;
        }

        /// <summary>Lê a pasta de novo e redesenha a lista.</summary>
        public void Refresh()
        {
            string folder = ScreenRecorder.Folder;
            _folder.text = FolderLabel + folder;
            List(folder, _clips);

            _list.Clear();
            if (_clips.Count == 0)
            {
                Label empty = MenuKit.Text(EmptyText, 18, MenuKit.Paper);
                empty.AddToClassList(RowClass);
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.marginTop = 10; empty.style.marginBottom = 10;
                _list.Add(empty);
                return;
            }

            foreach (Clip clip in _clips) _list.Add(Row(clip));
        }

        VisualElement Row(Clip clip)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;
            row.style.paddingLeft = 10; row.style.paddingRight = 6;
            row.style.paddingTop = 4; row.style.paddingBottom = 4;
            row.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);

            Label when = MenuKit.Text(clip.When.ToString("yyyy-MM-dd   HH:mm:ss", CultureInfo.InvariantCulture), 17, MenuKit.Paper);
            when.style.width = 250;
            when.AddToClassList(RowClass);
            row.Add(when);

            string length = clip.Recording ? RecordingNow : clip.Finished ? Duration(clip.Seconds) : Unfinished;
            Label time = MenuKit.Text(length, 17, clip.Recording ? MenuKit.Green : MenuKit.Yellow);
            time.style.width = 150;
            time.AddToClassList(RowClass);
            row.Add(time);

            Label size = MenuKit.Text(Size(clip.Bytes), 17, MenuKit.Paper);
            size.style.width = 110;
            size.style.opacity = 0.8f;
            size.AddToClassList(RowClass);
            row.Add(size);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            row.Add(spacer);

            // O clipe que está sendo gravado agora não se abre nem se apaga.
            if (clip.Recording) return row;

            if (_confirming == clip.Path)
            {
                Label ask = MenuKit.Text(ConfirmText, 16, MenuKit.Yellow);
                ask.AddToClassList(RowClass);
                ask.style.marginRight = 10;
                row.Add(ask);
                row.Add(Small(YesText, () => Delete(clip.Path), 130));
                row.Add(Small(NoText, () => { _confirming = null; Refresh(); }, 70));
            }
            else
            {
                row.Add(Small(PlayText, () => Open(clip.Path), 90));
                row.Add(Small(DeleteText, () => { _confirming = clip.Path; _message.text = ""; Refresh(); }, 100));
            }
            return row;
        }

        static Button Small(string text, Action onClick, float width)
        {
            Button button = MenuKit.MenuButton(text, onClick);
            button.style.width = width;
            button.style.height = 34;
            button.style.fontSize = 15;
            button.style.marginLeft = 6;
            return button;
        }

        // ---------------------------------------------------------------- Ações

        /// <summary>Abre o clipe no reprodutor do sistema.</summary>
        public void Open(string path)
        {
            if (!File.Exists(path)) { Refresh(); return; }
            OpenUrl(new Uri(path).AbsoluteUri);
        }

        /// <summary>Abre a pasta das gravações (criando-a, se ainda não existe).</summary>
        public void OpenFolder()
        {
            try
            {
                string folder = ScreenRecorder.Folder;
                Directory.CreateDirectory(folder);
                OpenUrl(new Uri(folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar).AbsoluteUri);
            }
            catch (Exception e)
            {
                _message.text = OpenFailed + e.Message;
            }
        }

        /// <summary>Apaga o clipe (já confirmado) e redesenha.</summary>
        public void Delete(string path)
        {
            _confirming = null;
            try
            {
                ScreenRecorder recorder = ScreenRecorder.Instance;
                if (recorder == null || !recorder.IsRecording || !SamePath(recorder.CurrentFile, path))
                    File.Delete(path);
                _message.text = "";
            }
            catch (Exception e)
            {
                _message.text = DeleteFailed + e.Message;
            }
            Refresh();
        }

        /// <summary>Pede a confirmação de apagar (o que o botão Delete faz). Testes.</summary>
        public void AskDelete(string path)
        {
            _confirming = path;
            Refresh();
        }

        /// <summary>Está pedindo confirmação para apagar este clipe.</summary>
        public bool Confirming(string path) => _confirming == path;

        void OpenUrl(string url)
        {
            if (OpenOverride != null) { OpenOverride(url); return; }
            try { Application.OpenURL(url); }
            catch (Exception e) { _message.text = OpenFailed + e.Message; }
        }

        // ---------------------------------------------------------------- Pasta

        /// <summary>Os clipes da pasta, o mais recente primeiro.</summary>
        public static void List(string folder, List<Clip> clips)
        {
            clips.Clear();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            ScreenRecorder recorder = ScreenRecorder.Instance;
            string current = recorder != null && recorder.IsRecording ? recorder.CurrentFile : null;

            string[] files;
            try { files = Directory.GetFiles(folder, ScreenRecorder.FilePrefix + "*" + ScreenRecorder.FileExtension); }
            catch (Exception) { return; }

            foreach (string path in files)
            {
                try
                {
                    var file = new FileInfo(path);
                    AviWriter.Info info = AviWriter.ReadInfo(path);
                    clips.Add(new Clip
                    {
                        Path = path,
                        When = StampOf(file),
                        Bytes = file.Length,
                        Seconds = info.Seconds,
                        Finished = info.Valid && info.Finished,
                        Recording = current != null && SamePath(current, path)
                    });
                }
                catch (Exception) { }
            }

            clips.Sort((a, b) => b.When != a.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(b.Path, a.Path));
        }

        // A hora do nome do arquivo (quando a gravação começou); sem ela, a do arquivo.
        static DateTime StampOf(FileInfo file)
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (name.StartsWith(ScreenRecorder.FilePrefix))
            {
                string stamp = name.Substring(ScreenRecorder.FilePrefix.Length);
                if (stamp.Length > ScreenRecorder.StampFormat.Length) stamp = stamp.Substring(0, ScreenRecorder.StampFormat.Length);
                if (DateTime.TryParseExact(stamp, ScreenRecorder.StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                    return when;
            }
            return file.LastWriteTime;
        }

        static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>"1:05" (minutos e segundos) ou "1:02:05".</summary>
        public static string Duration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return total >= 3600
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", total / 3600, total / 60 % 60, total % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        /// <summary>"850 KB" ou "12.4 MB", com ponto (o texto do jogo é em inglês).</summary>
        public static string Size(long bytes)
        {
            if (bytes < 1024 * 1024) return string.Format(CultureInfo.InvariantCulture, "{0} KB", Mathf.Max(1, Mathf.RoundToInt(bytes / 1024f)));
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (1024f * 1024f));
        }
    }
}
