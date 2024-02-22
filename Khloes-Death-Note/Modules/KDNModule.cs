using AForge.Imaging.Filters;
using AForge.Imaging;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System;

namespace Khloes_Death_Note.Modules
{
    public class KDNModule : InteractionModuleBase<SocketInteractionContext>
    {
        public Dictionary<string, List<string>> globalDutyList = new Dictionary<string, List<string>>();

        [SlashCommand("book", "Submit your WT book")]
        [RequireContext(ContextType.Guild)]
        public async Task book(IAttachment image, SocketGuildUser user = null)
        {
            await Context.Interaction.DeferAsync();

            var imgUrl = image.Url;
            await FullBooksearchAsync(imgUrl, Context.User.Username, Context.Interaction.Id.ToString());

            await Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = $"Your book has finished processing. Duty list count {globalDutyList.Count} - 1st list length {globalDutyList.FirstOrDefault().Value.Count}");
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
        }



        public async Task FullBooksearchAsync(string bookUrl, string username, string uniqueid)
        {
            // timer stuff
            var timerOverall = new Stopwatch();
            timerOverall.Start();
            List<long> timerDurations = new List<long>();

            // download the passed url & store in memory as a bitmap & save
            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(bookUrl);
            using var memoryStream = new MemoryStream(imageBytes);
            Bitmap bitmap = new Bitmap(memoryStream);
            bitmap.Save($"book_{username}_{uniqueid}.png");

            // get list of all tiles we're searching for
            List<string> pngFiles = Directory.GetFiles("tiles", "*.png").ToList();
            Console.WriteLine($"We'll be checking for {pngFiles.Count} tiles.");

            // init local duty list for this user
            List<string> localDutyList = new List<string>();

            object lockobject = new object();

            Parallel.ForEach(pngFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, pngFile =>
            {
                // per-tile timer
                var timerPerTile = new Stopwatch();
                timerPerTile.Start();

                // create local copies of each image to store within the context of each thread
                Bitmap tileLocalImage = new Bitmap(pngFile);
                Bitmap bookLocalImage = new Bitmap(pngFile);
                lock (lockobject)
                {
                    // this clones the original book image into a unique object to avoid thread locking issues
                    bookLocalImage = RetrieveCloneBitmap($"book_{username}_{uniqueid}.png");
                }
                var tileExistsInbook = FindTileInImage(bookLocalImage, tileLocalImage);

                timerPerTile.Stop();
                timerDurations.Add(timerPerTile.ElapsedMilliseconds);

                if (tileExistsInbook)
                {
                    Console.WriteLine($"Tile {pngFile} found in this book image.");
                    localDutyList.Add(pngFile);
                }

                tileLocalImage.Dispose();
                bookLocalImage.Dispose();
            });

            // Wait for all tasks to complete

            timerOverall.Stop();

            globalDutyList.Add(username, localDutyList);

            long avgTileDurations = new long();
            foreach (var timer in timerDurations)
            {
                avgTileDurations = avgTileDurations + timer;
            }
            if (timerDurations.Count == pngFiles.Count)
                avgTileDurations = avgTileDurations / timerDurations.Count;


            Console.WriteLine($"Took {timerOverall.ElapsedMilliseconds}ms to complete. Avg per-tile duration was {avgTileDurations}ms.");

        }


        public static bool FindTileInImage(Bitmap bookImage, Bitmap tileImage)
        {
            // Convert images to grayscale
            Grayscale filter = new Grayscale(0.2125, 0.7154, 0.0721);
            Bitmap grayBookImage = filter.Apply(bookImage);
            Bitmap grayTileImage = filter.Apply(tileImage);

            // Create template matching algorithm's instance
            ExhaustiveTemplateMatching tm = new ExhaustiveTemplateMatching(0.90f);

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
