using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using PlasmaShared;
using ZkData;

namespace ZeroKLobby
{
    class ServerImagesHandler
    {
        readonly string basePath;
        readonly Dictionary<string, Item> items = new Dictionary<string, Item>();

        readonly object locker = new object();

        public ServerImagesHandler(SpringPaths springPaths)
        {
            basePath = Utils.MakePath(springPaths.WritableDirectory, "LuaUI", "Configs");
        }

        public Image GetImage(string name)
        {
            var item = GetImageItem(name);
            if (item != null) return item.Image;
            else return null;
        }

        public Item GetImageItem(string name)
        {
            lock (locker)
            {
                Item item;
                items.TryGetValue(name, out item);
                if (item == null || item.IsError)
                {
                    item = new Item() { Name = name };
                    items[name] = item;
                    item.LocalPath = Utils.MakePath(basePath, name);
                    var dir = Path.GetDirectoryName(item.LocalPath);

                    try
                    {
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        if (File.Exists(item.LocalPath))
                        {
                            item.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(item.LocalPath)));
                            item.IsLoaded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning("Failed to load image:{0}", ex);
                    }

                    if (!item.IsLoaded || DateTime.Now.Subtract(File.GetLastWriteTime(item.LocalPath)).TotalDays > 3)
                    {
                        Task.Factory.StartNew((state) =>
                            {
                                var i = (Item)state;
                                var url = GlobalConst.BaseImageUrl + i.Name;
                                try
                                {
                                    using (var wc = new WebClient()) wc.DownloadFile(url, i.LocalPath);
                                    i.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(item.LocalPath)));
                                    i.IsLoaded = true;
                                    File.SetLastWriteTime(i.LocalPath, DateTime.Now);
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceWarning("Failed to load server image: {0}: {1}", url, ex);
                                    if (!i.IsLoaded) i.IsError = true;
                                }
                            },
                                              item);
                    }
                }
                return item;
            }
        }


        public class Item
        {
            public Image Image;
            public bool IsError;
            public bool IsLoaded;
            public string LocalPath;
            public string Name;
        }
    }
}