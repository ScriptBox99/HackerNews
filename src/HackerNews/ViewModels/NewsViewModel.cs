﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices;
using AsyncAwaitBestPractices.MVVM;
using Microsoft.Maui.Controls;

namespace HackerNews;

class NewsViewModel : BaseViewModel
{
	readonly WeakEventManager<string> _pullToRefreshEventManager = new();
	readonly TextAnalysisService _textAnalysisService;
	readonly HackerNewsAPIService _hackerNewsAPIService;

	bool _isListRefreshing;

	public NewsViewModel(TextAnalysisService textAnalysisService, HackerNewsAPIService hackerNewsAPIService)
	{
		_textAnalysisService = textAnalysisService;
		_hackerNewsAPIService = hackerNewsAPIService;

		RefreshCommand = new AsyncCommand(ExecuteRefreshCommand);

		//Ensure Observable Collection is thread-safe https://codetraveler.io/2019/09/11/using-observablecollection-in-a-multi-threaded-xamarin-forms-application/
		BindingBase.EnableCollectionSynchronization(TopStoryCollection, null, ObservableCollectionCallback);
	}

	public event EventHandler<string> PullToRefreshFailed
	{
		add => _pullToRefreshEventManager.AddEventHandler(value);
		remove => _pullToRefreshEventManager.RemoveEventHandler(value);
	}

	public ObservableCollection<StoryModel> TopStoryCollection { get; } = new ObservableCollection<StoryModel>();

	public ICommand RefreshCommand { get; }

	public bool IsListRefreshing
	{
		get => _isListRefreshing;
		set => SetProperty(ref _isListRefreshing, value);
	}

	async Task ExecuteRefreshCommand()
	{
		TopStoryCollection.Clear();

		try
		{
			await foreach (var story in GetTopStories(StoriesConstants.NumberOfStories).ConfigureAwait(false))
			{
				StoryModel? updatedStory = null;

				try
				{
					updatedStory = story with { TitleSentiment = await _textAnalysisService.GetSentiment(story.Title).ConfigureAwait(false) };
				}
				catch (Exception)
				{
					//Todo Add TextAnalysis API Key in TextAnalysisConstants.cs
					updatedStory = story;
				}
				finally
				{
					if (updatedStory is not null && !TopStoryCollection.Any(x => x.Title.Equals(updatedStory.Title)))
						InsertIntoSortedCollection(TopStoryCollection, (a, b) => b.Score.CompareTo(a.Score), updatedStory);
				}
			}
		}
		catch (Exception e)
		{
			OnPullToRefreshFailed(e.ToString());
		}
		finally
		{
			IsListRefreshing = false;
		}
	}

	async IAsyncEnumerable<StoryModel> GetTopStories(int? storyCount = int.MaxValue)
	{
		var topStoryIds = await _hackerNewsAPIService.GetTopStoryIDs().ConfigureAwait(false);
		var getTopStoryTaskList = topStoryIds.Select(_hackerNewsAPIService.GetStory).ToList();

		while (getTopStoryTaskList.Any() && storyCount-- > 0)
		{
			var completedGetStoryTask = await Task.WhenAny(getTopStoryTaskList).ConfigureAwait(false);
			getTopStoryTaskList.Remove(completedGetStoryTask);

			var story = await completedGetStoryTask.ConfigureAwait(false);
			yield return story;
		}
	}

	void InsertIntoSortedCollection<T>(ObservableCollection<T> collection, Comparison<T> comparison, T modelToInsert)
	{
		if (collection.Count is 0)
		{
			collection.Add(modelToInsert);
		}
		else
		{
			int index = 0;
			foreach (var model in collection)
			{
				if (comparison(model, modelToInsert) >= 0)
				{
					collection.Insert(index, modelToInsert);
					return;
				}

				index++;
			}

			collection.Insert(index, modelToInsert);
		}
	}

	//Ensure Observable Collection is thread-safe https://codetraveler.io/2019/09/11/using-observablecollection-in-a-multi-threaded-xamarin-forms-application/
	void ObservableCollectionCallback(IEnumerable collection, object context, Action accessMethod, bool writeAccess)
	{
		Device.BeginInvokeOnMainThread(accessMethod);
	}

	void OnPullToRefreshFailed(string message) => _pullToRefreshEventManager.RaiseEvent(this, message, nameof(PullToRefreshFailed));
}