using AForge.Imaging.Filters;
using AForge.Imaging;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System;
using Khloes_Death_Note.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text;

namespace Khloes_Death_Note.Modules
{
    public class KDNModule : InteractionModuleBase<SocketInteractionContext>
    {
        public KDNService _kdnService { get; set; }


        [SlashCommand("book", "Submit your WT book")]
        [RequireContext(ContextType.Guild)]
        public async Task book(IAttachment image, SocketGuildUser user = null)
        {
            // let the user know we're processing this
            await Context.Interaction.DeferAsync();

            // send the passed image attachment url to be processed - returns the number of tiles we found in the image
            var imgUrl = image.Url;
            var foundTiles = await FullBooksearchAsync(imgUrl, Context.User.Username, Context.Interaction.Id.ToString());

            var responseMsg = new StringBuilder();

            if (foundTiles == 0)
            {
                responseMsg.Append($"I checked your image, but I could not find any duties in it. Did you submit a Wondrous Tails book? If so, please send it to {Context.Channel.GetUserAsync(110866678161645568).Result.Mention}.");
            }

            if (foundTiles > 0 && foundTiles < 12)
            {
                responseMsg.Append($"I finished checking your book, but I only recognized {foundTiles} duties, so I'm probably missing a couple.");
            }

            if (foundTiles == 12)
            {
                responseMsg.Append($"I finished checking your book.");
            }

            await Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = responseMsg.ToString());
        }

        [SlashCommand("finished", "Clears out the current WT duty list")]
        [RequireContext(ContextType.Guild)]
        public async Task finished(string name, SocketGuildUser user = null)
        {
            
        }

        [SlashCommand("list", "Show the list of duties to be completed")]
        [RequireContext(ContextType.Guild)]
        public async Task list(string name, SocketGuildUser user = null)
        {
            user ??= (SocketGuildUser)Context.User;
            await user.ModifyAsync(x => x.Nickname = name);
            await RespondAsync($"{user.Mention} I changed your name to **{name}**");



            // remove path & extension from each string
            /*
            for (int i = 0; i < pngFiles.Count; i++)
            {
                pngFiles[i] = pngFiles[i].Substring(0, 6);
                pngFiles[i] = pngFiles[i].Substring(0, pngFiles[i].Length - 4);
            }
            */
        }



        public async Task<int> FullBooksearchAsync(string bookUrl, string username, string uniqueid)
        {
            // timer stuff
            var timerOverall = new Stopwatch();
            timerOverall.Start();
            List<long> timerDurations = new List<long>();

            // download the passed url, store in memory as a bitmap, save, & finally dispose
            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(bookUrl);
            using var memoryStream = new MemoryStream(imageBytes);
            Bitmap bookImage = new Bitmap(memoryStream);
            bookImage.Save($"book_{username}_{uniqueid}.png");
            bookImage.Dispose();

            // get list of all tiles we're searching for
            List<string> pngFiles = Directory.GetFiles("tiles", "*.png").ToList();
            Console.WriteLine($"We'll be checking for {pngFiles.Count} tiles.");

            // init local duty list for this book submission
            List<string> localDutyList = new List<string>();

            // black magic fuckery
            object lockobject = new object();
            // more black magic
            Parallel.ForEach(pngFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, pngFile =>
            {
                // per-tile timer
                var timerPerTile = new Stopwatch();
                timerPerTile.Start();

                // create local copies of each image to store within the context of each thread
                Bitmap tileLocalImage = new Bitmap(pngFile);
                Bitmap bookLocalImage = new Bitmap(pngFile); // declare it now because we cannot inside of the lock below
                lock (lockobject)
                {
                    // this clones the original book image into a unique object to avoid thread locking issues
                    bookLocalImage = RetrieveCloneBitmap($"book_{username}_{uniqueid}.png");
                }
                // check the book image for the given tile
                var tileExistsInbook = FindTileInImage(bookLocalImage, tileLocalImage);

                timerPerTile.Stop();
                timerDurations.Add(timerPerTile.ElapsedMilliseconds);

                // add tile if it's not in the duty list already
                if (tileExistsInbook)
                {
                    Console.WriteLine($"Tile {pngFile} found in this book image.");
                    localDutyList.Add(pngFile);
                }

                tileLocalImage.Dispose();
                bookLocalImage.Dispose();
            });

            // end timer stuff
            timerOverall.Stop();

            // add found tiles to the global duty list
            foreach (var duty in localDutyList)
            {
                // if global duty list doesn't contain this tile, add it
                if (!_kdnService.globalDutyList.ContainsKey(duty))
                {
                    _kdnService.globalDutyList.Add(duty, 1);
                    Console.WriteLine($"{duty} is a new duty; adding with count 1.");
                }
                // if global duty list contains this tile, find it in the global duty list and increment its count by 1
                else
                {
                    int thisDutyCount;
                    int thisDutyNewCount;

                    _kdnService.globalDutyList.TryGetValue(duty, out thisDutyCount);
                    thisDutyNewCount = thisDutyCount + 1;
                    _kdnService.globalDutyList[duty] = thisDutyNewCount;

                    Console.WriteLine($"{duty} is an existing duty; incrementing count to {thisDutyNewCount}.");
                }
            }

            // perform per-tile duration calcs for performance
            long avgTileDurations = new long();
            foreach (var timer in timerDurations)
            {
                avgTileDurations = avgTileDurations + timer;
            }
            if (timerDurations.Count == pngFiles.Count)
                avgTileDurations = avgTileDurations / timerDurations.Count;


            Console.WriteLine($"Took {timerOverall.ElapsedMilliseconds}ms to complete. Avg per-tile duration was {avgTileDurations}ms. Total duty count is {_kdnService.globalDutyList.Count}");

            // pass number of count tiles back to the command so we can ID if we may have missed any tiles from this book
            return localDutyList.Count;
        }


        // searches bookImage for tileImage - scales tileImage down if needed
        // thanks chatgpt
        public static bool FindTileInImage(Bitmap bookImage, Bitmap tileImage)
        {
            // Convert images to grayscale
            Grayscale filter = new Grayscale(0.2125, 0.7154, 0.0721);
            Bitmap grayBookImage = filter.Apply(bookImage);
            Bitmap grayTileImage = filter.Apply(tileImage);

            // Create template matching algorithm's instance
            ExhaustiveTemplateMatching tm = new ExhaustiveTemplateMatching(0.93f);

            // Initialize variables to store the best match
            Rectangle bestMatchRectangle = Rectangle.Empty;
            double bestMatchScore = 0;

            // Perform template matching at multiple scales
            for (double scale = 1.0; scale >= 0.59; scale -= 0.08) // Values above 0.08 appear to not function with book screenshots taken with lowest UI scaling size
            {
                // Resize the template image
                int newWidth = (int)(grayTileImage.Width * scale);
                int newHeight = (int)(grayTileImage.Height * scale);

                //Console.WriteLine($"Scaling set to {scale}. Width {newWidth} x Height {newHeight}");

                ResizeNearestNeighbor resizeFilter = new ResizeNearestNeighbor(newWidth, newHeight);
                Bitmap resizedTemplateImage = resizeFilter.Apply(grayTileImage);

                // Find the match
                TemplateMatch[] matches = tm.ProcessImage(grayBookImage, resizedTemplateImage);


                if (matches.Any())
                    return true;
            }

            return false;
        }

        private static Bitmap RetrieveCloneBitmap(string file_name)
        {
            using (Bitmap bm = new Bitmap(file_name))
            {
                return new Bitmap(bm);
            }
        }
    }
}
