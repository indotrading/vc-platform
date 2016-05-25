﻿angular.module('platformWebApp')
.controller('platformWebApp.modulesListController', ['$scope', 'filterFilter', 'platformWebApp.bladeNavigationService', 'platformWebApp.dialogService', 'platformWebApp.modules', 'uiGridConstants', 'platformWebApp.uiGridHelper', 'platformWebApp.moduleHelper', '$timeout',
function ($scope, filterFilter, bladeNavigationService, dialogService, modules, uiGridConstants, uiGridHelper, moduleHelper, $timeout) {
    $scope.uiGridConstants = uiGridConstants;
    var blade = $scope.blade;

    blade.refresh = function () {
        blade.isLoading = true;
        blade.parentBlade.refresh().then(function (data) {
            blade.currentEntities = blade.isGrouped ? moduleHelper.moduleBundles : data;
            blade.isLoading = false;
        })
    };

    blade.selectNode = function (node) {
        $scope.selectedNodeId = node.id;

        var newBlade = {
            id: 'moduleDetails',
            title: 'platform.blades.module-detail.title',
            currentEntity: node,
            controller: 'platformWebApp.moduleDetailController',
            template: '$(Platform)/Scripts/app/modularity/blades/module-detail.tpl.html'
        };

        bladeNavigationService.showBlade(newBlade, blade);
    };

    function isItemsChecked() {
        return $scope.gridApi && _.any($scope.gridApi.selection.getSelectedRows());
    }

    switch (blade.mode) {
        case 'update':
            blade.toolbarCommands = [{
                name: "platform.commands.update", icon: 'fa fa-arrow-up',
                executeMethod: function () { executeAction('update'); },
                canExecuteMethod: isItemsChecked,
                permission: 'platform:module:manage'
            }];
            break;
        case 'available':
            blade.toolbarCommands = [{
                name: "platform.commands.install", icon: 'fa fa-plus',
                executeMethod: function () { executeAction('install'); },
                canExecuteMethod: isItemsChecked,
                permission: 'platform:module:manage'
            }];
            break;
    }

    function executeAction(action) {
        var selection = _.where($scope.gridApi.selection.getSelectedRows(), { isInstalled: false });
        if (_.any(selection)) {
            bladeNavigationService.closeChildrenBlades(blade, function () {
                blade.isLoading = true;

                // eliminate duplicating nodes, if any
                var grouped = _.groupBy(selection, 'id');
                selection = [];
                _.each(grouped, function (vals) {
                    selection.push(_.last(vals));
                });

                modules.getDependencies(selection, function (data) {
                    blade.isLoading = false;

                    var dialog = {
                        id: "confirm",
                        action: action,
                        selection: selection,
                        dependencies: data,
                        callback: function () {
                            _.each(selection, function (x) {
                                if (!_.findWhere(data, { id: x.id })) {
                                    data.push(x);
                                }
                            });
                            modules.install(data, onAfterConfirmed, function (error) {
                                bladeNavigationService.setError('Error ' + error.status, blade);
                            });
                        }
                    }
                    dialogService.showDialog(dialog, '$(Platform)/Scripts/app/modularity/dialogs/moduleAction-dialog.tpl.html', 'platformWebApp.confirmDialogController');
                }, function (error) {
                    bladeNavigationService.setError('Error ' + error.status, blade);
                });
            });
        }
    }

    function onAfterConfirmed(data) {
        var newBlade = {
            id: 'moduleInstallProgress',
            currentEntity: data,
            title: blade.title,
            controller: 'platformWebApp.moduleInstallProgressController',
            template: '$(Platform)/Scripts/app/modularity/wizards/newModule/module-wizard-progress-step.tpl.html'
        };
        bladeNavigationService.showBlade(newBlade, blade);
    }


    // ui-grid
    $scope.setGridOptions = function (gridOptions) {
        switch (blade.mode) {
            case 'update':
                _.extend(gridOptions, {
                    showTreeRowHeader: false
                });
                break;
            case 'installed':
                _.extend(gridOptions, {
                    showTreeRowHeader: false,
                    selectionRowHeaderWidth: 0,
                    enableRowSelection: false,
                    enableSelectAll: false
                });
                break;
        }

        uiGridHelper.initialize($scope, gridOptions, function (gridApi) {
            gridApi.grid.registerRowsProcessor($scope.singleFilter, 90);

            if (blade.mode === 'available') {
                $scope.$watch('blade.isGrouped', function (isGrouped) {
                    if (isGrouped) {
                        blade.currentEntities = moduleHelper.moduleBundles;
                        if (!_.any($scope.gridApi.grouping.getGrouping().grouping)) {
                            $scope.gridApi.grouping.groupColumn('$group');
                        }
                        $timeout($scope.gridApi.treeBase.expandAllRows);
                    } else {
                        blade.currentEntities = moduleHelper.availableModules;
                        $scope.gridApi.grouping.clearGrouping();
                    }
                });

                $scope.toggleRow = function (row) {
                    $scope.gridApi.treeBase.toggleRowTreeState(row);
                };

                $scope.getGroupInfo = function (groupEntity) {
                    return _.values(groupEntity)[0];
                };
            }
        });
    };

    $scope.singleFilter = function (renderableRows) {
        var visibleCount = 0;
        renderableRows.forEach(function (row) {
            row.visible = _.any(filterFilter([row.entity], blade.searchText));
            if (row.visible) visibleCount++;
        });

        $scope.filteredEntitiesCount = visibleCount;
        return renderableRows;
    };


    blade.isLoading = false;
}]);