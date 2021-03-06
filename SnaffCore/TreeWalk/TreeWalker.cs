﻿using Classifiers;
using SnaffCore.Concurrency;
using SnaffCore.FileScan;
using System;
using System.Collections.Generic;
using System.IO;
using static SnaffCore.Config.Options;

namespace SnaffCore.TreeWalk
{
    public class TreeWalker
    {
        private BlockingMq Mq { get; set; }
        private BlockingStaticTaskScheduler FileTaskScheduler { get; set; }

        public TreeWalker(string shareRoot)
        {
            Mq = BlockingMq.GetMq();

            FileTaskScheduler = SnaffCon.GetFileTaskScheduler();

            if (shareRoot == null)
            {
                Mq.Trace("A null made it into TreeWalker. Wtf.");
                return;
            }

            Mq.Trace("About to start a TreeWalker on share " + shareRoot);
            WalkTree(shareRoot);
            Mq.Trace("Finished TreeWalking share " + shareRoot);
        }

        public void WalkTree(string shareRoot)
        {
            try
            {
                // Walks a tree checking files and generating results as it goes.
                Stack<string> dirs = new Stack<string>(20);

                if (!Directory.Exists(shareRoot))
                {
                    return;
                }

                dirs.Push(shareRoot);

                while (dirs.Count > 0)
                {
                    string currentDir = dirs.Pop();
                    string[] subDirs;
                    try
                    {
                        subDirs = Directory.GetDirectories(currentDir);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        //Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        //Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (Exception e)
                    {
                        Mq.Trace(e.ToString());
                        continue;
                    }

                    string[] files = null;
                    try
                    {
                        files = Directory.GetFiles(currentDir);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        //Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        //Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (Exception e)
                    {
                        Mq.Trace(e.ToString());
                        continue;
                    }

                    // check if we actually like the files
                    foreach (string file in files)
                    {
                        FileTaskScheduler.New(() =>
                        {
                            try
                            {
                                FileScanner fileScanner = new FileScanner(file);
                            }
                            catch (Exception e)
                            {
                                Mq.Trace(e.ToString());
                            }
                        });
                    }

                    // Push the subdirectories onto the stack for traversal if they aren't on any discard-lists etc.
                    foreach (string dirStr in subDirs)
                    {
                        foreach (ClassifierRule classifier in MyOptions.DirClassifiers)
                        {
                            try
                            {
                                DirClassifier dirClassifier = new DirClassifier(classifier);
                                DirResult dirResult = dirClassifier.ClassifyDir(dirStr);
                                // TODO: concurrency uplift: when there is a pooled concurrency queue, just add the dir as a job to the queue
                                if (dirResult.ScanDir)
                                {
                                    dirs.Push(dirStr);
                                }
                            }
                            catch (Exception e)
                            {
                                Mq.Trace(e.ToString());
                                continue;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Mq.Error(e.ToString());
            }
        }
    }
}