﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeoRemote
{
	static class SlideDownloader
	{
		const int DownloaderCount = 20;

		static Task task = null;
		static CancellationTokenSource token = null;

		static Task ThreadPoolRunAsync(Action action)
		{
			var tcs = new TaskCompletionSource<object>();
			ThreadPool.QueueUserWorkItem(state =>
			{
				try
				{
					action();
					tcs.SetResult(null);
				}
				catch (Exception ex) { tcs.SetException(ex); }
			});
			return tcs.Task;
		}

		async public static void Run(string slidesQuery, string size, Actions actions)
		{
			if (task != null)
			{
				token.Cancel();
				await task;
				token = null;
				task = null;
			}

			var regex = new Regex($@"^{nameof(NeoRemote)}-Slide-[0-9a-f]{{32}}\.bmp$$", RegexOptions.IgnoreCase);
			Directory.EnumerateFiles(Settings.SlidesPath).Where(file => regex.IsMatch(Path.GetFileName(file))).ToList().ForEach(file => File.Delete(file));
			actions.ClearSlides();

			if (string.IsNullOrWhiteSpace(slidesQuery))
				return;

			token = new CancellationTokenSource();
			task = DownloadSlides(slidesQuery, size, actions, token.Token);
		}

		async static Task<List<TOutput>> RunTasks<TInput, TOutput>(IEnumerable<TInput> input, Func<TInput, Task<TOutput>> func, CancellationToken token)
		{
			var tasks = new HashSet<Task<TOutput>>();
			var results = new List<TOutput>();
			var enumerator = input.GetEnumerator();
			while (true)
			{
				while ((tasks.Count < DownloaderCount) && (!token.IsCancellationRequested) && (enumerator.MoveNext()))
					tasks.Add(func(enumerator.Current));

				if (!tasks.Any())
					break;

				var finished = await Task.WhenAny(tasks.ToArray());
				results.Add(finished.Result);
				tasks.Remove(finished);
			}
			return results;
		}

		async static Task RunTasks<TInput>(IEnumerable<TInput> input, Func<TInput, Task> func, CancellationToken token) => await RunTasks(input, async item => { await func(item); return false; }, token);

		async static Task DownloadSlides(string slidesQuery, string size, Actions actions, CancellationToken token)
		{
			var queries = slidesQuery.Split('\n').ToList();

			var urls = (await RunTasks(queries, query => GetSlideURLs(query, size, token), token)).SelectMany(x => x).ToList();

			var random = new Random();
			urls = urls.OrderBy(x => random.Next()).ToList();

			await RunTasks(urls, url => FetchSlide(url, actions, token), token);
		}

		async static Task<List<string>> GetSlideURLs(string query, string size, CancellationToken token)
		{
			try
			{
				var url = $"https://www.google.com/search?q={Uri.EscapeUriString(query)}&tbm=isch&tbs=isz:lt,islt:{size}";
				var data = await URLDownloader.GetURLString(url, token);
				return Regex.Matches(data, @"""ou"":""(.*?)""").Cast<Match>().Select(match => match.Groups[1].Value).ToList();
			}
			catch { return new List<string>(); }
		}

		async static Task FetchSlide(string url, Actions actions, CancellationToken token)
		{
			string md5;
			using (var md5cng = new MD5Cng())
				md5 = BitConverter.ToString(md5cng.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "");
			var fileName = $@"{Settings.SlidesPath}\{nameof(NeoRemote)}-Slide-{md5}.bmp";

			if (!File.Exists(fileName))
			{
				try
				{
					using (var ms = await URLDownloader.GetURLData(url, token))
						await ThreadPoolRunAsync(() => ShrinkSlide(ms, fileName, token));
				}
				catch { File.WriteAllBytes(fileName, new byte[] { }); }
			}

			var fileInfo = new FileInfo(fileName);
			if ((fileInfo.Exists) && (fileInfo.Length != 0))
				actions.EnqueueSlides(new List<string> { fileName });
		}

		static void ShrinkSlide(Stream stream, string outputFileName, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			var slide = new BitmapImage();
			slide.BeginInit();
			slide.StreamSource = stream;
			slide.CacheOption = BitmapCacheOption.OnLoad;
			slide.EndInit();

			token.ThrowIfCancellationRequested();

			var scale = Math.Min(SystemParameters.PrimaryScreenWidth / slide.PixelWidth, SystemParameters.PrimaryScreenHeight / slide.PixelHeight);
			var resizedSlide = new TransformedBitmap(slide, new ScaleTransform(scale, scale));

			var encoder = new BmpBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(resizedSlide));
			using (var output = File.Create(outputFileName))
				encoder.Save(output);
		}
	}
}
