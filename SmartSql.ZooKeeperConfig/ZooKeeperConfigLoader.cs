﻿using SmartSql.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using SmartSql.SqlMap;
using org.apache.zookeeper;
using System.Threading.Tasks;
using System.IO;
using System.Xml.Serialization;
using System.Xml;
using SmartSql.Abstractions.Logging;
using SmartSql.Abstractions.Config;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SmartSql.ZooKeeperConfig
{
    /// <summary>
    /// ZooKeeper 配置加载器
    /// </summary>
    public class ZooKeeperConfigLoader : ConfigLoader
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly ZooKeeperManager zooKeeperManager;
        public String ConnectString { get; private set; }
        public ZooKeeper ZooClient
        {
            get
            {
                return zooKeeperManager.Instance;
            }
        }
        public ZooKeeperConfigLoader(String connStr) : this(NullLoggerFactory.Instance, connStr)
        {

        }
        public ZooKeeperConfigLoader(ILoggerFactory loggerFactory
            , String connStr
            ) : this(loggerFactory, new CreateOptions
            {
                ConnectionString = connStr
            })
        {

        }
        public ZooKeeperConfigLoader(ILoggerFactory loggerFactory, CreateOptions options)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<ZooKeeperConfigLoader>();
            zooKeeperManager = new ZooKeeperManager(loggerFactory, options);
        }


        public override SmartSqlMapConfig Load(string path, ISmartSqlMapper smartSqlMapper)
        {
            var loadTask = LoadAsync(path, smartSqlMapper);
            loadTask.Wait();
            return loadTask.Result;
        }


        public async Task<SmartSqlMapConfig> LoadAsync(string path, ISmartSqlMapper smartSqlMapper)
        {
            _logger.LogDebug($"SmartSql.ZooKeeperConfigLoader Load: {path} Starting");
            var configResult = await ZooClient.getDataAsync(path, new SmartSqlMapConfigWatcher(_loggerFactory, smartSqlMapper, this));
            var configStream = new ConfigStream
            {
                Path = path,
                Stream = new MemoryStream(configResult.Data)
            };
            var config = LoadConfig(configStream, smartSqlMapper);

            foreach (var sqlmapSource in config.SmartSqlMapSources)
            {
                switch (sqlmapSource.Type)
                {
                    case SmartSqlMapSource.ResourceType.File:
                        {
                            var sqlmap = await LoadSmartSqlMapAsync(sqlmapSource.Path, config);
                            config.SmartSqlMaps.Add(sqlmap);
                            break;
                        }
                    case SmartSqlMapSource.ResourceType.Directory:
                        {
                            var sqlmapChildren = await ZooClient.getChildrenAsync(sqlmapSource.Path);
                            foreach (var sqlmapChild in sqlmapChildren.Children)
                            {
                                var sqlmapPath = $"{sqlmapSource.Path}/{sqlmapChild}";
                                var sqlmap = await LoadSmartSqlMapAsync(sqlmapPath, config);
                                config.SmartSqlMaps.Add(sqlmap);
                            }
                            break;
                        }
                    default:
                        {
                            throw new ArgumentException("unknow SmartSqlMap.Type!");
                        }
                }

            }
            _logger.LogDebug($"SmartSql.ZooKeeperConfigLoader Load: {path} End");

            smartSqlMapper.LoadConfig(config);
            return config;
        }

        public async Task<SmartSqlMap> LoadSmartSqlMapAsync(String path, SmartSqlMapConfig config)
        {
            _logger.LogDebug($"SmartSql.LoadSmartSqlMapAsync Load: {path}");
            var sqlmapResult = await ZooClient.getDataAsync(path, new SmartSqlMapWatcher(_loggerFactory, config, this));
            var sqlmapStream = new ConfigStream
            {
                Path = path,
                Stream = new MemoryStream(sqlmapResult.Data)
            };
            return LoadSmartSqlMap(sqlmapStream, config);
        }


        public override void Dispose()
        {
            zooKeeperManager.Dispose();
        }
    }

}
