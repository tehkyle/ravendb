class databaseStatistics {
    countOfDocuments = ko.observable<number>();
    databaseId = ko.observable<string>();

    countOfDocumentsText = ko.observable<string>();
    countOfIndexesText = ko.observable<string>();
    staleFilteredIndexesCountText = ko.observable<string>();
    countOfFilteredIndexesText = ko.observable<string>();
    staleIndexesCountText = ko.observable<string>();
    errorsCountText = ko.observable<string>();
    alertsCountText = ko.observable<string>();
    tasksCountText = ko.observable<string>();
    hasAttachments = ko.observable<boolean>();
    countOfAttachmentsText = ko.observable<string>();
    
    fromDto(dto: reducedDatabaseStatisticsDto) {
        this.countOfDocuments(dto.CountOfDocuments);
        this.databaseId(dto.DatabaseId);

        this.countOfFilteredIndexesText(this.getItemCountText(dto.CountOfIndexesExcludingDisabledAndAbandoned, "index", "es"));
        this.staleFilteredIndexesCountText(dto.CountOfStaleIndexesExcludingDisabledAndAbandoned.toLocaleString() + " stale");
        this.errorsCountText(this.getItemCountText(dto.CountOfErrors, "error", "s"));
        this.countOfDocumentsText(this.getItemCountText(dto.CountOfDocuments, "document", "s"));
        this.countOfIndexesText(this.getItemCountText(dto.CountOfIndexes, "index", "es"));
        this.staleIndexesCountText(dto.CountOfStaleIndexes.toLocaleString() + " stale");
        this.alertsCountText(this.getItemCountText(dto.CountOfAlerts, "alert", "s"));

        this.tasksCountText(this.getItemCountText(dto.ApproximateTaskCount, "task", "s"));
        this.hasAttachments(dto.CountOfAttachments > 0);
        this.countOfAttachmentsText(this.getItemCountText(dto.CountOfAttachments, "attachment", "s"));
    }

    private getItemCountText(itemCount: number, singularText: string, suffix: string): string {
        if (!itemCount) {
            itemCount = 0;
        }
        var itemCountText = itemCount.toLocaleString() + " " + singularText;
        if (itemCount !== 1) {
            itemCountText += suffix;
        }
        return itemCountText;
    }
}

export = databaseStatistics;
