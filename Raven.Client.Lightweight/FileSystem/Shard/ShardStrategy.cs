﻿using Raven.Client.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Client.FileSystem.Shard
{
	/// <summary>
	/// Default shard strategy for the sharding document store
	/// </summary>
	public class ShardStrategy
	{
        private readonly IDictionary<string, IAsyncFilesCommands> shards;

        public delegate string ModifyFileNameFunc(FilesConvention convention, string shardId, string filename);

        public ShardStrategy(IDictionary<string, IAsyncFilesCommands> shards)
		{
			if (shards == null) throw new ArgumentNullException("shards");
			if (shards.Count == 0)
				throw new ArgumentException("Shards collection must have at least one item", "shards");

            var shardsDicToList = shards.Select((x) => new
            {
                Id = x.Value.GetServerIdAsync(),
                Key = x.Key,
                Url = x.Value.UrlFor(),

            }).ToList();


            shardsDicToList.ForEach(x =>
            {
                try { x.Id.Wait(); }
                // If server is down we don't want it to stop us
                // from going, so catch it.
                catch { }
            });


            var multiKeysUrl = shardsDicToList.Where(x => x.Id.Status == TaskStatus.RanToCompletion).GroupBy(x => new
            {
                ID = x.Id.Result,
                x.Url
            }, x => x.Key)
            .Where(x => x.Count() > 1).Select(x => x.Key.Url).FirstOrDefault();

            if (multiKeysUrl != null)
                throw new NotSupportedException(string.Format("Multiple keys in shard dictionary for {0} are not supported.", multiKeysUrl));

            this.shards = new Dictionary<string, IAsyncFilesCommands>(shards, StringComparer.OrdinalIgnoreCase);


			Conventions = shards.First().Value.Conventions.Clone();

			ShardAccessStrategy = new SequentialShardAccessStrategy();
			ShardResolutionStrategy = new DefaultShardResolutionStrategy(shards.Keys, this);
			ModifyFileName = (convention, shardId, documentId) => convention.IdentityPartsSeparator + shardId + convention.IdentityPartsSeparator + documentId;
		}

        public FilesConvention Conventions { get; set; }

		/// <summary>
		/// Gets or sets the shard resolution strategy.
		/// </summary>
		public IShardResolutionStrategy ShardResolutionStrategy { get; set; }

		/// <summary>
		/// Gets or sets the shard access strategy.
		/// </summary>
		public IShardAccessStrategy ShardAccessStrategy { get; set; }

		/// <summary>
		/// Get or sets the modification for the document id for sharding
		/// </summary>
		public ModifyFileNameFunc ModifyFileName { get; set; }

        public IDictionary<string, IAsyncFilesCommands> Shards
		{
			get { return shards; }
		}

		public int StableHashString(string text)
		{
			unchecked
			{
				return text.ToCharArray().Aggregate(11, (current, c) => current * 397 + c);
			}
		}
	}
}
