using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace NCM2MP3
{
    public partial class Form1 : Form
    {
        byte[] coreKey  = new byte[] { 0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57};
        byte[] metaKey = new byte[] { 0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28};
        byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A};

        string[] path = new string[0];
        public Form1()
        {
            InitializeComponent();
            textBox1.ReadOnly = true;
            button1.Enabled = false;           
        }

        private void button1_Click(object sender, EventArgs e)
        {
            foreach (var ii in path)
            {
                try
                {
                    textBox2.AppendText($"转换{ii}\r\n");
                    Trans(ii);
                    textBox2.AppendText("成功\r\n");
                }
                catch (Exception ex)
                {
                    textBox2.AppendText(ex.Message + "\r\n");
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.Filter = "ncm文件|*.ncm";
            if(DialogResult.OK == openFile.ShowDialog())
            {
                if (openFile.FileNames.Length > 0)
                {
                    if(openFile.FileNames.Length == 1)
                    {
                        textBox1.Text = openFile.FileNames[0];
                    }
                    else
                    {
                        textBox1.Text = $"选择了{openFile.FileNames.Length}个文件";
                    }
                    path = openFile.FileNames;
                    button1.Enabled = true;
                }
            }
        }

        private void Trans(string path)
        {
            if(!File.Exists(path))
            {
                return;
            }
            var allbytes = File.ReadAllBytes(path);
            var fs = File.OpenRead(path);
            var header = new byte[8];

            // read header
            fs.Read(header, 0, header.Length);
            Debug.WriteLine(Encoding.ASCII.GetString(header));

            // skip 2 character
            fs.Seek(2, SeekOrigin.Current);
            var keylength = new byte[4];

            // read key length
            fs.Read(keylength, 0, keylength.Length);
            Debug.WriteLine(Encoding.ASCII.GetString(keylength));

            // sort array then combine then to a new hex string then parse to int 
            Array.Sort(keylength);
            var keylen = Convert.ToInt32("0x" + string.Join("", keylength.Select(s => s.ToString("X"))), 16);
            var keydata = new byte[keylen];

            // read key data
            fs.Read(keydata, 0, keylen);
            for (int i = 0; i < keydata.Length; i++)
            {
                keydata[i] ^= 0x64;
            }

            // decrypt key data
            using (Aes aes = Aes.Create())
            {
                aes.Key = coreKey;
                aes.Mode = CipherMode.ECB;
                var decrypter = aes.CreateDecryptor();
                var resultdata = decrypter.TransformFinalBlock(keydata, 0, keydata.Length).Skip(17).ToArray();
                keydata = resultdata;
            }
            var key_box = Enumerable.Range(0, 256).Select(s => (byte)s).ToArray();
            byte c = 0;
            byte lastbyte = 0;
            byte offset = 0;
            for (int i = 0; i < key_box.Length; i++)
            {
                var swap = key_box[i];
                c = (byte)((swap + lastbyte + keydata[offset]) & 0xff);
                offset++;
                if (offset >= keydata.Length)
                {
                    offset = 0;
                }
                key_box[i] = key_box[c];
                key_box[c] = swap;
                lastbyte = c;
            }

            // read meta length
            var meta_length = new byte[4];
            fs.Read(meta_length, 0, meta_length.Length);
            Array.Sort(meta_length);
            var meta_len = Convert.ToInt32("0x" + string.Join("", meta_length.Select(s => s.ToString("X"))), 16);
            var meta_data = new byte[meta_len];
            fs.Read(meta_data, 0, meta_data.Length);
            for (int i = 0; i < meta_data.Length; i++)
            {
                meta_data[i] ^= 0x63;
            }
            meta_data = Convert.FromBase64String(Encoding.ASCII.GetString(meta_data.Skip(22).ToArray()));
            string format = "mp3";

            // decrypt meta data which is a json contains info of the song
            using (Aes aes = Aes.Create())
            {
                aes.Key = metaKey;
                aes.Mode = CipherMode.ECB;
                var decrypter = aes.CreateDecryptor();
                meta_data = decrypter.TransformFinalBlock(meta_data, 0, meta_data.Length);
                var json = Encoding.UTF8.GetString(meta_data).Replace("music:", "");
                var jobj = Newtonsoft.Json.Linq.JObject.Parse(json);
                Newtonsoft.Json.Linq.JToken jToken = null;
                jobj.TryGetValue("format", out jToken);
                if(jToken != null)
                {
                    format = jToken.ToString(); ;
                }
            }
            var crc32bytes = new byte[4];
            fs.Read(crc32bytes, 0, crc32bytes.Length);
            Array.Sort(crc32bytes);
            var crc32len = Convert.ToInt32("0x" + string.Join("", crc32bytes.Select(s => s.ToString("X"))), 16);

            // skip 5 character, 
            fs.Seek(5, SeekOrigin.Current);
            var imagelenbytes = new byte[4];

            // read image length
            fs.Read(imagelenbytes, 0, imagelenbytes.Length);
            Array.Sort(imagelenbytes);
            var imagelen = Convert.ToInt32("0x" + string.Join("", imagelenbytes.Select(s => s.ToString("X"))), 16);
            var imagedata = new byte[imagelen];

            // read image data
            fs.Read(imagedata, 0, imagedata.Length);

            // remained is song content
            var chunk = new byte[1];
            string newpath = path.Substring(0, path.LastIndexOf("."));
            using (FileStream ofs = new FileStream(newpath + "." + format, FileMode.Create))
            {
                while (true)
                {
                    var temp = new byte[0x8000];
                    var len = fs.Read(temp, 0, temp.Length);
                    if (len <= 0)
                    {
                        break;
                    }
                    chunk = new byte[len];
                    Array.Copy(temp, chunk, len);
                    for (int i = 1; i < chunk.Length + 1; i++)
                    {
                        var j = i & 0xff;
                        chunk[i - 1] ^= key_box[(key_box[j] + key_box[(key_box[j] + j) & 0xff]) & 0xff];
                    }
                    ofs.Write(chunk, 0, chunk.Length);
                }
            }
            fs.Close();
        }
    }
}
