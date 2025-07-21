// Copyright (c) Imazen LLC.
// No part of this project, including this file, may be copied, modified,
// propagated, or distributed except as permitted in COPYRIGHT.txt.
// Licensed under the Apache License, Version 2.0.

namespace Imazen.Common.Issues
{
    public class IssueSink: IIssueProvider,IIssueReceiver {
        private readonly string defaultSource;
        private readonly int? maxIssues  = null;
        public IssueSink(string defaultSource, int maxIssues) {
            this.defaultSource = defaultSource;
            this.maxIssues = maxIssues;
        }
        public IssueSink(string defaultSource) {
            this.defaultSource = defaultSource;
        }
        private readonly IDictionary<int, IIssue> issueSet = new Dictionary<int,IIssue>();
        private readonly IList<IIssue> issues = new List<IIssue>();
        private readonly object issueSync = new object();
        /// <summary>
        /// Returns a copy of the list of reported issues.
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerable<IIssue> GetIssues() {
            lock (issueSync) {
                return new List<IIssue>(issues);
            }
        }  

        public virtual void ClearIssues() {
            lock (issueSync) {
                issues.Clear();
                issueSet.Clear();
            }
        }
        /// <summary>
        /// Adds the specified issue to the list unless it is an exact duplicate of another instance.
        /// </summary>
        /// <param name="i"></param>
        public virtual void AcceptIssue(IIssue i) {
            //Set default source value
            if (i.Source == null && i is Issue issue) issue.Source = defaultSource;

            //Perform duplicate checking, then add item if unique.
            var hash = i.Hash();
            lock (issueSync)
            {
                if (issueSet.ContainsKey(hash)) return;
                if (maxIssues.HasValue && issueSet.Count >= maxIssues.Value) return;
                issueSet[hash] = i;
                issues.Add(i);
            }
        }
    }
}
